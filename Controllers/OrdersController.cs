using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Demo.Models;
using MimeKit;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Net;

namespace Demo.Controllers
{
    public class OrdersController : Controller
    {
        private readonly DB _context;
        private readonly IConfiguration _config;

        public OrdersController(DB context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // GET: Orders
        public async Task<IActionResult> Index(bool? isPaid, int page = 1, int pageSize = 15)
        {
            var query = _context.Orders
                .Include(o => o.Table)
                .Include(o => o.Member)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                .AsQueryable();

            if (isPaid.HasValue)
            {
                query = query.Where(o => o.IsPaid == isPaid.Value);
            }

            // 总记录数
            int totalOrders = await query.CountAsync();

            // 分页取数据
            var orders = await query
                .OrderBy(o => o.OrderDate)   // 排序
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // 传到前端
            ViewBag.IsPaid = isPaid;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalOrders = totalOrders;
            ViewBag.TotalPages = (int)Math.Ceiling(totalOrders / (double)pageSize);

            return View(orders);
        }




        // GET: Orders/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var order = await _context.Orders
                .Include(o => o.Table)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (order == null)
            {
                return NotFound();
            }

            return View(order);
        }

        // GET: Orders/Create
        public IActionResult Create()
        {
            ViewData["TableId"] = new SelectList(_context.Tables, "Id", "Name");
            ViewData["Products"] = new SelectList(_context.Products, "Id", "Name");
            return View();
        }


        // POST: Orders/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        // POST: Orders/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,TableId,MemberId,IsPaid,OrderDate,IsTakeAway,OrderItemsTemp")] Order order)
        {
            // 自动设置订单时间
            order.OrderDate = DateTime.Now;

            // 如果前端有传 MemberId，则在服务器端验证该 Member 是否存在
            if (order.MemberId.HasValue)
            {
                var memberExists = await _context.Members.AnyAsync(m => m.Id == order.MemberId.Value);
                if (!memberExists)
                {
                    ModelState.AddModelError("MemberId", "Invalid MemberId. Member does not exist.");
                }
            }

            // Dine In必须选择桌子
            if (!order.IsTakeAway && !order.TableId.HasValue)
            {
                ModelState.AddModelError("TableId", "Please select a table for dine-in orders.");
            }

            // 外带不能选择桌子
            if (order.IsTakeAway && order.TableId.HasValue)
            {
                ModelState.AddModelError("TableId", "Takeaway orders cannot have a table.");
            }

            // 必须至少有一个商品
            if (order.OrderItemsTemp == null || !order.OrderItemsTemp.Any())
            {
                ModelState.AddModelError("OrderItemsTemp", "Please add at least one item to the order.");
            }

            // 如果验证失败，返回视图
            if (!ModelState.IsValid)
            {
                ViewData["TableId"] = new SelectList(_context.Tables.Where(t => t.IsAvailable), "Id", "Name", order.TableId);
                ViewData["Products"] = new SelectList(_context.Products, "Id", "Name");
                return View(order);
            }

            // 外带强制清空 TableId
            if (order.IsTakeAway)
            {
                order.TableId = null;
            }

            _context.Add(order);
            await _context.SaveChangesAsync();

            // 保存 OrderItems
            foreach (var item in order.OrderItemsTemp)
            {
                var product = await _context.Products.FindAsync(item.ProductId);
                if (product == null) continue;

                decimal unitPrice;
                bool isVoucher = item.IsVoucher;
                int? memberVoucherId = item.MemberVoucherId;

                if (isVoucher && item.MemberVoucherId.HasValue)
                {
                    // 找到对应的会员代金券
                    var memberVoucher = await _context.MemberVouchers
                        .Include(mv => mv.Voucher)
                        .FirstOrDefaultAsync(mv => mv.Id == item.MemberVoucherId);

                    if (memberVoucher != null && !memberVoucher.IsUsed)
                    {
                        unitPrice = memberVoucher.Voucher.DiscountedPrice; 
                        memberVoucher.IsUsed = true;                       
                    }
                    else
                    {
                        continue; // 无效代金券，跳过
                    }
                }
                else
                {
                    unitPrice = product.Price; // 正常商品用原价
                }

                var newItem = new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = unitPrice,
                    TotalPrice = unitPrice * item.Quantity,
                    IsVoucher = isVoucher,
                    MemberVoucherId = memberVoucherId
                };

                _context.OrderItems.Add(newItem);
            }
            await _context.SaveChangesAsync();


            // 如果订单已付款并且有会员关联，添加积分
            if (order.IsPaid && order.MemberId.HasValue)
            {
                // 计算订单总金额的整数部分
                decimal totalAmount = order.OrderItems.Sum(item => item.TotalPrice);

                int pointsToAdd = (int)Math.Floor(totalAmount);

                // 找到会员并添加积分
                var member = await _context.Members.FindAsync(order.MemberId.Value);
                if (member != null)
                {
                    member.Point += pointsToAdd;
                    await _context.SaveChangesAsync();
                }
            }

            // 堂食 → 占用桌子
            if (!order.IsTakeAway && order.TableId.HasValue)
            {
                var table = await _context.Tables.FindAsync(order.TableId);
                if (table != null)
                {
                    table.IsAvailable = false;
                    await _context.SaveChangesAsync();
                }
            }

            return RedirectToAction(nameof(Index));
        }



        // GET: Orders/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var order = await _context.Orders
                .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            
            ViewData["TableId"] = new SelectList(_context.Tables, "Id", "Name", order.TableId);
            ViewData["Products"] = new SelectList(_context.Products, "Id", "Name");

            
            ViewBag.ProductsList = await _context.Products
                .Select(p => new { Id = p.Id, Name = p.Name, Price = p.Price })
                .ToListAsync();

            return View(order);
        }



        // POST: Orders/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Order order)
        {
            if (id != order.Id)
            {
                return NotFound();
            }

            var existingOrder = await _context.Orders
                .Include(o => o.OrderItems)
                .Include(o => o.Table)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (existingOrder == null)
            {
                return NotFound();
            }

            // 验证 MemberId 是否存在
            if (order.MemberId.HasValue)
            {
                var memberExists = await _context.Members.AnyAsync(m => m.Id == order.MemberId.Value);
                if (!memberExists)
                {
                    TempData["InvalidMemberId"] = true;
                    return RedirectToAction(nameof(Edit), new { id = order.Id });
                }
            }

            var wasPaid = existingOrder.IsPaid;

            try
            {
                // 更新基本字段
                existingOrder.IsPaid = order.IsPaid;
                existingOrder.IsTakeAway = order.IsTakeAway;
                existingOrder.OrderDate = order.OrderDate;
                existingOrder.MemberId = order.MemberId;
                existingOrder.TableId = order.IsTakeAway ? null : order.TableId;

                // 分离已有的 voucher 与 非 voucher
                var existingVoucherItems = existingOrder.OrderItems.Where(oi => oi.IsVoucher).ToList();
                var existingNonVoucherItems = existingOrder.OrderItems.Where(oi => !oi.IsVoucher).ToList();

                // 把原来的非 voucher 全部删除（我们会根据表单重新添加）
                _context.OrderItems.RemoveRange(existingNonVoucherItems);

                // 从表单中找出被提交（保留）的 voucher 的 Id 列表（没有 Id 的视为新项）
                var postedVoucherIds = order.OrderItemsTemp?
                    .Where(i => i.IsVoucher && i.Id != 0)
                    .Select(i => i.Id)
                    .ToList() ?? new List<int>();

                
                var voucherToRemove = existingVoucherItems.Where(v => !postedVoucherIds.Contains(v.Id)).ToList();
                foreach (var vr in voucherToRemove)
                {
                    if (vr.MemberVoucherId.HasValue)
                    {
                        var mv = await _context.MemberVouchers.FindAsync(vr.MemberVoucherId.Value);
                        if (mv != null)
                        {
                            mv.IsUsed = false; // 释放Voucher
                        }
                    }
                    _context.OrderItems.Remove(vr);
                }

                
                var voucherToKeep = existingVoucherItems.Where(v => postedVoucherIds.Contains(v.Id)).ToList();
                foreach (var voucherItem in voucherToKeep)
                {
                    _context.Entry(voucherItem).State = EntityState.Unchanged;
                }

                // 处理表单里提交的新非 voucher 项（或编辑后重新添加）
                if (order.OrderItemsTemp != null && order.OrderItemsTemp.Any())
                {
                    foreach (var item in order.OrderItemsTemp)
                    {
                        // 如果是 voucher（无论是否有 Id），这里跳过（voucher 已由上面处理）
                        if (item.IsVoucher) continue;

                        var product = await _context.Products.FindAsync(item.ProductId);
                        if (product == null) continue;

                        var newItem = new OrderItem
                        {
                            OrderId = existingOrder.Id,
                            ProductId = item.ProductId,
                            Quantity = item.Quantity,
                            UnitPrice = product.Price,
                            TotalPrice = product.Price * item.Quantity,
                            IsVoucher = false
                        };

                        _context.OrderItems.Add(newItem);
                    }
                }

                // 如果订单现在已付款且是堂食，释放餐桌
                if (existingOrder.IsPaid && !existingOrder.IsTakeAway && existingOrder.TableId != null)
                {
                    var table = await _context.Tables.FindAsync(existingOrder.TableId);
                    if (table != null)
                    {
                        table.IsAvailable = true;
                    }
                }


                await _context.SaveChangesAsync();

                // 如果订单从未付款变为已付款，并且有会员关联 
                // 发送 Receipt 到会员 Email
                if (!wasPaid && existingOrder.IsPaid && existingOrder.MemberId.HasValue)
                {
                    
                    var orderItems = await _context.OrderItems
                        .Include(oi => oi.Product)
                        .Where(oi => oi.OrderId == existingOrder.Id)
                        .ToListAsync();

                    var totalAmount = orderItems.Sum(oi => oi.TotalPrice);
                    int pointsToAdd = (int)Math.Floor(totalAmount);
                    var member = await _context.Members.FindAsync(existingOrder.MemberId.Value);
                    if (member != null)
                    {
                        member.Point += pointsToAdd;
                        await _context.SaveChangesAsync();

                        // 发送 Receipt 邮件（失败不阻塞主流程）
                        try
                        {
                            var html = GenerateReceiptHtml(existingOrder, orderItems, member);
                            await SendEmailAsync(member.Email, $"Receipt for Order #{existingOrder.Id}", html);
                        }
                        catch (Exception ex)
                        {
                            // 发送失败时只记录到 TempData（或你可以把 ex.Message log 出来）
                            TempData["EmailError"] = "Receipt email failed to send: " + ex.Message;
                        }
                    }
                }

                //TempData["Success"] = "Order updated successfully!";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Orders.Any(e => e.Id == order.Id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return RedirectToAction(nameof(Index));
        }



        // GET: Orders/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var order = await _context.Orders
                .Include(o => o.Table)
                .Include(o => o.Member)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.MemberVoucher)
                        .ThenInclude(mv => mv.Voucher)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (order == null)
            {
                return NotFound();
            }

            return View(order);
        }

        // POST: Orders/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order != null)
            {
                _context.Orders.Remove(order);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool OrderExists(int id)
        {
            return _context.Orders.Any(e => e.Id == id);
        }

        //Order 历史记录
        // 在 OrdersController.cs 中替换原来的 History 方法为下面内容
        // Order 历史记录（带分页）
        public async Task<IActionResult> History(int page = 1, int pageSize = 10)
        {
            var email = HttpContext.Session.GetString("MemberEmail");
            if (string.IsNullOrEmpty(email))
            {
                return RedirectToAction("Login", "Login");
            }

            var member = await _context.Members.FirstOrDefaultAsync(m => m.Email == email);
            if (member == null)
            {
                return RedirectToAction("Login", "Login");
            }

            // 基础查询（只筛选当前会员）
            var query = _context.Orders
                .Where(o => o.MemberId == member.Id)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                .OrderByDescending(o => o.OrderDate)
                .AsQueryable();

            // 计算总数与总页数
            int totalOrders = await query.CountAsync();
            int totalPages = (int)Math.Ceiling(totalOrders / (double)pageSize);

            // 防止 page 越界
            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            // 分页读取
            var orders = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // 传前端用于分页显示
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalOrders = totalOrders;
            ViewBag.TotalPages = totalPages;

            return View(orders);
        }


        public async Task<IActionResult> CurrentOrder()
        {
            // 获取当前用户身份
            int? memberId = null;
            string guestId = null;
            bool isMember = false;

            if (HttpContext.Session.GetString("MemberEmail") != null)
            {
                var email = HttpContext.Session.GetString("MemberEmail");
                var member = await _context.Members.FirstOrDefaultAsync(m => m.Email == email);
                if (member != null)
                {
                    memberId = member.Id;
                    isMember = true;
                }
            }
            else
            {
                guestId = HttpContext.Session.GetString("GuestId");
            }

            // 获取用餐方式
            string diningOption = HttpContext.Session.GetString("DiningOption") ?? "TakeAway";
            bool isTakeAway = diningOption == "TakeAway";

            // 查找当前用户的未付款订单
            Order currentOrder = null;

            if (isMember && memberId.HasValue)
            {
                currentOrder = await _context.Orders
                    .Include(o => o.Table)
                    .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                    .FirstOrDefaultAsync(o => o.MemberId == memberId && !o.IsPaid && o.IsTakeAway == isTakeAway);
            }
            else if (!string.IsNullOrEmpty(guestId))
            {
                currentOrder = await _context.Orders
                    .Include(o => o.Table)
                    .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                    .FirstOrDefaultAsync(o => o.GuestId == guestId && !o.IsPaid && o.IsTakeAway == isTakeAway);
            }

            if (currentOrder == null)
            {
                ViewBag.Message = "No current order found.";
            }

            return View(currentOrder);
        }

        [HttpGet]
        public async Task<IActionResult> CheckUnpaidOrder()
        {
            // 获取当前用户身份
            int? memberId = null;
            string guestId = null;

            if (HttpContext.Session.GetString("MemberEmail") != null)
            {
                var email = HttpContext.Session.GetString("MemberEmail");
                var member = await _context.Members.FirstOrDefaultAsync(m => m.Email == email);
                if (member != null)
                {
                    memberId = member.Id;
                }
            }
            else
            {
                guestId = HttpContext.Session.GetString("GuestId");
            }

            // 获取用餐方式
            string diningOption = HttpContext.Session.GetString("DiningOption") ?? "TakeAway";
            bool isTakeAway = diningOption == "TakeAway";

            // 查找当前用户的未付款订单
            bool hasUnpaidOrder = false;

            if (memberId.HasValue)
            {
                hasUnpaidOrder = await _context.Orders
                    .AnyAsync(o => o.MemberId == memberId && !o.IsPaid && o.IsTakeAway == isTakeAway);
            }
            else if (!string.IsNullOrEmpty(guestId))
            {
                hasUnpaidOrder = await _context.Orders
                    .AnyAsync(o => o.GuestId == guestId && !o.IsPaid && o.IsTakeAway == isTakeAway);
            }

            return Json(new { hasUnpaidOrder });
        }

        [HttpGet]
        public async Task<IActionResult> Search(string type, string keyword, int page = 1, int pageSize = 10)
        {
            var query = _context.Orders
                .Include(o => o.Table)
                .Include(o => o.Member)
                .Include(o => o.OrderItems) 
                .AsQueryable();

            if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(keyword))
            {
                keyword = keyword.ToLower();

                switch (type)
                {
                    case "orderId":
                        if (!string.IsNullOrEmpty(keyword))
                        {
                            query = query.Where(o => o.Id.ToString().Contains(keyword));
                        }
                        break;

                    case "member":
                        query = query.Where(o =>
                            (o.Member != null &&
                             (o.Member.Id.ToString().Contains(keyword) ||
                              o.Member.Username.ToLower().Contains(keyword)))
                            || (o.Member == null && "guest".Contains(keyword)));
                        break;

                    case "table":
                        query = query.Where(o =>
                            (o.Table != null && o.Table.Name.ToLower().Contains(keyword))
                            || (o.IsTakeAway && "takeaway".Contains(keyword)));
                        break;

                    case "paid":
                        if (keyword == "paid")
                            query = query.Where(o => o.IsPaid);
                        else if (keyword == "unpaid")
                            query = query.Where(o => !o.IsPaid);
                        break;
                }
            }

            int totalCount = await query.CountAsync();
            int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var orders = await query
                .OrderByDescending(o => o.OrderDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var html = new System.Text.StringBuilder();
            html.Append("<table class='order-table'><thead><tr>");
            html.Append("<th>Order ID</th><th>Member</th><th>Table</th><th>Order Date</th><th>Total</th><th>Paid</th><th>Actions</th>");
            html.Append("</tr></thead><tbody>");

            foreach (var order in orders)
            {
                html.Append("<tr>");
                html.Append($"<td>{order.Id}</td>");
                html.Append($"<td>{(order.Member != null ? $"#{order.Member.Id} ({System.Net.WebUtility.HtmlEncode(order.Member.Username)})" : "Guest")}</td>");
                html.Append($"<td>{System.Net.WebUtility.HtmlEncode(order.Table?.Name ?? "Take Away")}</td>");
                html.Append($"<td>{order.OrderDate:g}</td>");

                
                html.Append($"<td>RM {order.TotalPrice:F2}</td>");

                html.Append($"<td>{(order.IsPaid ? "<span class='badge badge-success'>Paid</span>" : "<span class='badge badge-danger'>Unpaid</span>")}</td>");
                html.Append("<td>");
                html.Append($"<a href='/Orders/Details/{order.Id}' class='btn btn-info btn-sm'>Details</a> ");
                html.Append($"<a href='/Orders/Edit/{order.Id}' class='btn btn-warning btn-sm'>Edit</a> ");
                html.Append($"<a href='/Orders/Delete/{order.Id}' class='btn btn-danger btn-sm'>Delete</a>");
                html.Append("</td>");
                html.Append("</tr>");
            }

            if (!orders.Any())
            {
                
                html.Append("<tr><td colspan='7'><div class='alert alert-info'>No orders found.</div></td></tr>");
            }

            html.Append("</tbody></table>");

            if (totalPages > 1)
            {
                html.Append("<div class='pagination'>");

                if (page > 1)
                {
                    html.Append($"<button class='page-btn' data-page='{page - 1}'>Prev</button>");
                }

                for (int i = 1; i <= totalPages; i++)
                {
                    var active = i == page ? " active" : "";
                    html.Append($"<button class='page-btn{active}' data-page='{i}'>{i}</button>");
                }

                if (page < totalPages)
                {
                    html.Append($"<button class='page-btn' data-page='{page + 1}'>Next</button>");
                }

                html.Append("</div>");
            }

            return Content(html.ToString(), "text/html");
        }

        //Receipt
        private string GenerateReceiptHtml(Order order, List<OrderItem> items, Member member)
        {
            var sb = new StringBuilder();
            sb.Append($"<h2>Receipt - Order #{order.Id}</h2>");
            sb.Append($"<p>Order Date: {order.OrderDate:yyyy-MM-dd HH:mm}</p>");
            sb.Append($"<p>Member: {WebUtility.HtmlEncode(member.Username)} (#{member.Id})</p>");
            sb.Append("<table style='width:100%; border-collapse: collapse;'>");
            sb.Append("<thead>");
            sb.Append("<tr>");
            sb.Append("<th style='border-bottom:1px solid #ddd; text-align:left; padding:6px;'>Product</th>");
            sb.Append("<th style='border-bottom:1px solid #ddd; padding:6px;'>Qty</th>");
            sb.Append("<th style='border-bottom:1px solid #ddd; padding:6px; text-align:right;'>Unit</th>");
            sb.Append("<th style='border-bottom:1px solid #ddd; padding:6px; text-align:right;'>Total</th>");
            sb.Append("</tr>");
            sb.Append("</thead>");
            sb.Append("<tbody>");
            foreach (var it in items)
            {
                var name = WebUtility.HtmlEncode(it.Product?.Name ?? ("Product #" + it.ProductId));
                sb.Append("<tr>");
                sb.Append($"<td style='padding:6px 0;'>{name}</td>");
                sb.Append($"<td style='text-align:center'>{it.Quantity}</td>");
                sb.Append($"<td style='text-align:right'>RM {it.UnitPrice:F2}</td>");
                sb.Append($"<td style='text-align:right'>RM {it.TotalPrice:F2}</td>");
                sb.Append("</tr>");
            }
            sb.Append("</tbody>");
            var grand = items.Sum(i => i.TotalPrice);
            sb.Append("<tfoot>");
            sb.Append("<tr>");
            sb.Append("<td colspan='3' style='text-align:right; padding-top:8px; font-weight:bold;'>Grand Total</td>");
            sb.Append($"<td style='text-align:right; padding-top:8px; font-weight:bold;'>RM {grand:F2}</td>");
            sb.Append("</tr>");
            sb.Append("</tfoot>");
            sb.Append("</table>");
            sb.Append("<p>Thank you for your purchase!</p>");
            return sb.ToString();
        }

        private async Task SendEmailAsync(string to, string subject, string htmlBody)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_config["Smtp:DisplayName"] ?? "X Burger", _config["Smtp:From"]));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            var host = _config["Smtp:Host"];
            var port = int.Parse(_config["Smtp:Port"] ?? "587");
            var user = _config["Smtp:User"];
            var pass = _config["Smtp:Pass"];
            var useSsl = bool.TryParse(_config["Smtp:UseSsl"], out var s) && s;

            var socketOptions = useSsl
                ? MailKit.Security.SecureSocketOptions.SslOnConnect
                : MailKit.Security.SecureSocketOptions.StartTls;

            await client.ConnectAsync(host, port, socketOptions);

            if (!string.IsNullOrEmpty(user))
            {
                await client.AuthenticateAsync(user, pass);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }





    }
}

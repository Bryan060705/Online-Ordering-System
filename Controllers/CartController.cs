using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Demo.Models;
using Newtonsoft.Json; 

namespace Demo.Controllers
{
    public class CartController : Controller
    {
        private readonly DB _context;

        public CartController(DB context)
        {
            _context = context;
        }

        // 显示购物车
        public IActionResult Index()
        {
            var cart = GetCart();
            return View(cart);
        }

        // 添加到购物车
        [HttpPost]
        public IActionResult AddToCart(int productId, int quantity)
        {
            var product = _context.Products
                .Include(p => p.Images)
                .FirstOrDefault(p => p.Id == productId);

            if (product == null) return NotFound();

            var cart = GetCart();

            
            var existingItem = cart.FirstOrDefault(c => c.ProductId == productId && !c.IsVoucherApplied);
            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
            }
            else
            {
                cart.Add(new CartItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    UnitPrice = product.Price,
                    Quantity = quantity,
                    ImagePath = product.Images?.FirstOrDefault()?.ImagePath ?? "/images/no-image.png",
                    IsVoucherApplied = false
                });
            }

            SaveCart(cart);
            return Json(new { success = true, message = "Added to cart!" });
        }


        private List<CartItem> GetCart()
        {
            var cartJson = HttpContext.Session.GetString("Cart");
            if (string.IsNullOrEmpty(cartJson))
            {
                return new List<CartItem>();
            }
            return JsonConvert.DeserializeObject<List<CartItem>>(cartJson) ?? new List<CartItem>();
        }

        private void SaveCart(List<CartItem> cart)
        {
            var cartJson = JsonConvert.SerializeObject(cart);
            HttpContext.Session.SetString("Cart", cartJson);
        }

        //写是CheckOut但是就是ConfirmOrder
        [HttpPost]
        public IActionResult Checkout()
        {
            var cart = GetCart();
            if (!cart.Any())
            {
                TempData["Error"] = "Your cart is empty!";
                return RedirectToAction("Index");
            }

            int? memberId = null;
            bool isMember = false;
            if (HttpContext.Session.GetString("MemberEmail") != null)
            {
                var email = HttpContext.Session.GetString("MemberEmail");
                var member = _context.Members.FirstOrDefault(m => m.Email == email);
                if (member != null)
                {
                    memberId = member.Id;
                    isMember = true;
                }
            }
            else
            {
               
                if (string.IsNullOrEmpty(HttpContext.Session.GetString("GuestId")))
                {
                    HttpContext.Session.SetString("GuestId", Guid.NewGuid().ToString());
                }
            }

            // 用餐方式
            string diningOption = HttpContext.Session.GetString("DiningOption") ?? "TakeAway";
            bool isTakeAway = diningOption == "TakeAway";

            // 查找用户未付款订单
            Order? existingOrder = null;
            if (isMember)
            {
                existingOrder = _context.Orders
                    .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                    .FirstOrDefault(o => o.MemberId == memberId && !o.IsPaid && o.IsTakeAway == isTakeAway);
            }
            else
            {
                var guestId = HttpContext.Session.GetString("GuestId");
                existingOrder = _context.Orders
                    .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                    .FirstOrDefault(o => o.GuestId == guestId && !o.IsPaid && o.IsTakeAway == isTakeAway);
            }

            // 没有未付款订单，则新建 
            if (existingOrder == null)
            {
                existingOrder = new Order
                {
                    MemberId = memberId,
                    GuestId = isMember ? null : HttpContext.Session.GetString("GuestId"),
                    IsTakeAway = isTakeAway,
                    OrderDate = DateTime.Now,
                    IsPaid = false
                };

                // 如果是 DineIn ，随机分配一张可用且能容纳该人数的桌子
                if (!isTakeAway)
                {
                    int diningCapacity = HttpContext.Session.GetInt32("DiningCapacity") ?? 1;
                    var availableTables = _context.Tables
                        .Where(t => t.IsAvailable && t.Capacity >= diningCapacity)
                        .ToList();

                    if (availableTables.Any())
                    {
                        var random = new Random();
                        var table = availableTables[random.Next(availableTables.Count)];
                        existingOrder.TableId = table.Id;
                        table.IsAvailable = false; // 标记桌子为已占用
                    }
                    else
                    {
                        TempData["Error"] = "No available tables for Dine In!";
                        return RedirectToAction("Index");
                    }
                }



                _context.Orders.Add(existingOrder);
                _context.SaveChanges(); 
            }

            // 把购物车里的商品加入Order
            foreach (var item in cart)
            {
                // Voucher 类型的 cart item
                if (item.IsVoucherApplied && item.MemberVoucherId.HasValue)
                {
                    var memberVoucher = _context.MemberVouchers
                        .Include(mv => mv.Voucher)
                        .FirstOrDefault(mv => mv.Id == item.MemberVoucherId.Value &&
                                             (memberId.HasValue ? mv.MemberId == memberId : true) &&
                                             !mv.IsUsed &&
                                             mv.ExpiryDate > DateTime.Now);

                    if (memberVoucher != null && memberVoucher.Voucher != null && memberVoucher.Voucher.ProductId == item.ProductId)
                    {
                        
                        var existingVoucherOrderItem = existingOrder.OrderItems
                            .FirstOrDefault(oi => oi.ProductId == item.ProductId && oi.IsVoucher && oi.MemberVoucherId == item.MemberVoucherId);

                        if (existingVoucherOrderItem != null)
                        {
                            existingVoucherOrderItem.Quantity += item.Quantity;
                            existingVoucherOrderItem.TotalPrice = existingVoucherOrderItem.UnitPrice * existingVoucherOrderItem.Quantity;
                        }
                        else
                        {
                            var orderItem = new OrderItem
                            {
                                ProductId = item.ProductId,
                                Quantity = item.Quantity,
                                UnitPrice = memberVoucher.Voucher.DiscountedPrice,
                                TotalPrice = memberVoucher.Voucher.DiscountedPrice * item.Quantity,
                                IsVoucher = true,
                                MemberVoucherId = item.MemberVoucherId
                            };

                            existingOrder.OrderItems.Add(orderItem);
                        }

                        // 标记 voucher 为已使用（只有在真正加入订单时）
                        memberVoucher.IsUsed = true;
                    }
                    else
                    {
                        // voucher 无效 -> 作为普通商品按原价加入
                        var product = _context.Products.Find(item.ProductId);
                        if (product != null)
                        {
                            var orderItem = new OrderItem
                            {
                                ProductId = item.ProductId,
                                Quantity = item.Quantity,
                                UnitPrice = product.Price,
                                TotalPrice = product.Price * item.Quantity
                            };
                            existingOrder.OrderItems.Add(orderItem);
                        }
                    }
                }
                else
                {
                    
                    var existingNonVoucher = existingOrder.OrderItems.FirstOrDefault(oi => oi.ProductId == item.ProductId && !oi.IsVoucher);
                    if (existingNonVoucher != null)
                    {
                        existingNonVoucher.Quantity += item.Quantity;
                        existingNonVoucher.TotalPrice = existingNonVoucher.UnitPrice * existingNonVoucher.Quantity;
                    }
                    else
                    {
                        var orderItem = new OrderItem
                        {
                            ProductId = item.ProductId,
                            Quantity = item.Quantity,
                            UnitPrice = item.UnitPrice,
                            TotalPrice = item.UnitPrice * item.Quantity,
                            IsVoucher = false
                        };

                        existingOrder.OrderItems.Add(orderItem);
                    }
                }
            }


            _context.SaveChanges();

            // 清空购物车 
            SaveCart(new List<CartItem>());

            TempData["Success"] = "Checkout successful! Your order has been saved.";
            return RedirectToAction("Index");
        }



        // PayOrder（付款并释放餐桌）
        [HttpPost]
        public IActionResult PayOrder(int orderId)
        {
            var order = _context.Orders
                .Include(o => o.Table)
                .FirstOrDefault(o => o.Id == orderId);

            if (order == null)
            {
                TempData["Error"] = "Order not found!";
                return RedirectToAction("Index");
            }

            order.IsPaid = true;

            // 如果是 Dine-In，释放餐桌
            if (!order.IsTakeAway && order.Table != null)
            {
                order.Table.IsAvailable = true;
            }

            _context.SaveChanges();

            TempData["Success"] = "Payment successful! Thank you.";
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        public IActionResult ApplyVoucher(int productId, int memberVoucherId)
        {
            var email = HttpContext.Session.GetString("MemberEmail");
            if (string.IsNullOrEmpty(email))
            {
                return Json(new { success = false, message = "Please login first" });
            }

            var member = _context.Members.FirstOrDefault(m => m.Email == email);
            if (member == null)
            {
                return Json(new { success = false, message = "Member not found" });
            }

            var memberVoucher = _context.MemberVouchers
                .Include(mv => mv.Voucher)
                .FirstOrDefault(mv => mv.Id == memberVoucherId && mv.MemberId == member.Id && !mv.IsUsed && mv.ExpiryDate > DateTime.Now);

            if (memberVoucher == null)
            {
                return Json(new { success = false, message = "Voucher not found or already used/expired" });
            }

            if (memberVoucher.Voucher == null || memberVoucher.Voucher.ProductId != productId)
            {
                return Json(new { success = false, message = "This voucher is not for this product" });
            }

            var cart = GetCart();

            
            var nonVoucherItem = cart.FirstOrDefault(c => c.ProductId == productId && !c.IsVoucherApplied);

            
            if (nonVoucherItem == null)
            {
                var product = _context.Products.Include(p => p.Images).FirstOrDefault(p => p.Id == productId);
                var voucherCartItem = new CartItem
                {
                    ProductId = productId,
                    ProductName = product?.Name ?? "",
                    UnitPrice = memberVoucher.Voucher.DiscountedPrice,
                    Quantity = 1,
                    ImagePath = product?.Images?.FirstOrDefault()?.ImagePath ?? "/images/no-image.png",
                    IsVoucherApplied = true,
                    MemberVoucherId = memberVoucherId,
                    VoucherName = memberVoucher.Voucher.Name
                };
                cart.Add(voucherCartItem);
                SaveCart(cart);
                return Json(new { success = true, message = "Voucher applied successfully" });
            }

            
            if (nonVoucherItem.Quantity > 1)
            {
                nonVoucherItem.Quantity -= 1;
            }
            else
            {
                
                cart.Remove(nonVoucherItem);
            }

            var productForImage = _context.Products.Include(p => p.Images).FirstOrDefault(p => p.Id == productId);
            var newVoucherItem = new CartItem
            {
                ProductId = productId,
                ProductName = productForImage?.Name ?? "",
                UnitPrice = memberVoucher.Voucher.DiscountedPrice,
                Quantity = 1,
                ImagePath = productForImage?.Images?.FirstOrDefault()?.ImagePath ?? "/images/no-image.png",
                IsVoucherApplied = true,
                MemberVoucherId = memberVoucherId,
                VoucherName = memberVoucher.Voucher.Name
            };
            cart.Add(newVoucherItem);
            SaveCart(cart);

            return Json(new { success = true, message = "Voucher applied successfully" });
        }


        // 移除商品
        [HttpPost]
        public IActionResult RemoveFromCart(int productId, int? memberVoucherId = null)
        {
            var cart = GetCart();
            CartItem item = null;
            if (memberVoucherId.HasValue)
            {
                item = cart.FirstOrDefault(c => c.MemberVoucherId == memberVoucherId.Value);
            }
            else
            {
                item = cart.FirstOrDefault(c => c.ProductId == productId && !c.IsVoucherApplied);
            }

            if (item != null)
            {
                cart.Remove(item);
                SaveCart(cart);
            }
            return RedirectToAction("Index");
        }

        // 移除Voucher
        [HttpPost]
        public IActionResult RemoveVoucher(int memberVoucherId)
        {
            var cart = GetCart();
            var cartItem = cart.FirstOrDefault(c => c.MemberVoucherId == memberVoucherId);
            if (cartItem == null)
            {
                return Json(new { success = false, message = "Product not found in cart" });
            }

            cart.Remove(cartItem);
            SaveCart(cart);

            return Json(new { success = true, message = "Voucher removed successfully" });
        }

        [HttpPost]
        public IActionResult UpdateQuantity(int productId, int quantity, int? memberVoucherId = null)
        {
            if (quantity < 1 || quantity > 99)
            {
                return Json(new { success = false, message = "Quantity must be between 1 and 99." });
            }

            var cart = GetCart();

            CartItem item;
            if (memberVoucherId.HasValue)
            {
                item = cart.FirstOrDefault(c => c.MemberVoucherId == memberVoucherId.Value);
            }
            else
            {
                
                item = cart.FirstOrDefault(c => c.ProductId == productId && !c.IsVoucherApplied);
            }

            if (item == null)
            {
                return Json(new { success = false, message = "Product not found in cart." });
            }

            if (item.IsVoucherApplied)
            {
                return Json(new { success = false, message = "Cannot change quantity for voucher items." });
            }

            item.Quantity = quantity;
            SaveCart(cart);

            return Json(new
            {
                success = true,
                itemTotal = item.Total,
                cartTotal = cart.Sum(c => c.Total)
            });
        }


        // POST: /Cart/AddVoucherToCart
        [HttpPost]
        public IActionResult AddVoucherToCart(int memberVoucherId)
        {
            var email = HttpContext.Session.GetString("MemberEmail");
            if (string.IsNullOrEmpty(email))
            {
                return Json(new { success = false, message = "Please login first" });
            }

            var member = _context.Members.FirstOrDefault(m => m.Email == email);
            if (member == null) return Json(new { success = false, message = "Member not found" });

            var memberVoucher = _context.MemberVouchers
                .Include(mv => mv.Voucher)
                .FirstOrDefault(mv => mv.Id == memberVoucherId && mv.MemberId == member.Id);

            if (memberVoucher == null) return Json(new { success = false, message = "Voucher not found" });
            if (memberVoucher.IsUsed) return Json(new { success = false, message = "This voucher is already used" });
            if (memberVoucher.ExpiryDate <= DateTime.Now) return Json(new { success = false, message = "This voucher is expired" });

            var voucher = memberVoucher.Voucher;
            if (voucher == null || !voucher.ProductId.HasValue) return Json(new { success = false, message = "Voucher has no linked product" });

            var product = _context.Products
                .Include(p => p.Images)
                .FirstOrDefault(p => p.Id == voucher.ProductId.Value);

            if (product == null) return Json(new { success = false, message = "Product not found" });

            var cart = GetCart();

            // 如果同一个 memberVoucher 已经在购物车里，拒绝重复加入
            if (cart.Any(c => c.IsVoucherApplied && c.MemberVoucherId == memberVoucherId))
            {
                return Json(new { success = false, message = "This voucher item is already in your cart" });
            }

            // 新增一行 voucher 类型的 cart item（数量固定 1）
            var cartItem = new CartItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = voucher.DiscountedPrice,
                Quantity = 1,
                ImagePath = product.Images?.FirstOrDefault()?.ImagePath ?? "/images/no-image.png",
                IsVoucherApplied = true,
                MemberVoucherId = memberVoucherId,
                VoucherName = voucher.Name
            };

            cart.Add(cartItem);
            SaveCart(cart);

            return Json(new { success = true, message = "Voucher item added to cart" });
        }




    }
}


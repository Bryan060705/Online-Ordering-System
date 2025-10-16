// Controllers/MembersController.cs
using Demo.Models;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using QRCoder;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Demo.Controllers
{
    public class MembersController : Controller
    {
        private readonly DB _context;
        private readonly IConfiguration _config;

        public MembersController(DB context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // GET: Members
        public async Task<IActionResult> Index()
        {
            return View(await _context.Members.ToListAsync());
        }

        // GET: Members/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var member = await _context.Members.FirstOrDefaultAsync(m => m.Id == id);
            if (member == null) return NotFound();
            return View(member);
        }

        // GET: Members/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Members/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Username,Password,Email,Point,PhotoPath")] Member member, IFormFile? Photo)
        {
            if (ModelState.IsValid)
            {
                if (string.IsNullOrEmpty(member.Password))
                {
                    ModelState.AddModelError("Password", "Password is required");
                    return View(member);
                }

                // 检查 Email 是否已存在
                if (await _context.Members.AnyAsync(m => m.Email == member.Email))
                {
                    ModelState.AddModelError("Email", "This email is already registered.");
                    return View(member);
                }

                // 检查Email Format
                if (!new EmailAddressAttribute().IsValid(member.Email))
                {
                    ModelState.AddModelError("Email", "Invalid Email format");
                    return View(member);
                }

                // hash password
                var hasher = new PasswordHasher<Member>();
                member.PasswordHash = hasher.HashPassword(member, member.Password);

                member.IsEmailConfirmed = true;

                if (Photo != null && Photo.Length > 0)
                {
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "members");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                    var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(Photo.FileName);
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await Photo.CopyToAsync(stream);
                    }

                    member.PhotoPath = "/uploads/members/" + uniqueFileName;
                }
                else
                {
                    member.PhotoPath = "/images/default.png";
                }

                _context.Add(member);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(member);
        }

        // GET: Members/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var member = await _context.Members.FindAsync(id);
            if (member == null) return NotFound();
            return View(member);
        }

        // POST: Members/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Username,Password,Email,Point,PhotoPath")] Member member, IFormFile? Photo)
        {
            if (id != member.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var existingMember = await _context.Members.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
                    if (existingMember == null) return NotFound();

                    // 如果上傳了新照片 -> 刪除舊的並儲存新照片
                    if (Photo != null && Photo.Length > 0)
                    {
                        if (!string.IsNullOrEmpty(existingMember.PhotoPath)
    && existingMember.PhotoPath.StartsWith("/uploads/members/"))
                        {
                            var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", existingMember.PhotoPath.TrimStart('/'));
                            if (System.IO.File.Exists(oldFilePath)) System.IO.File.Delete(oldFilePath);
                        }

                        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "members");
                        if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                        var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(Photo.FileName);
                        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await Photo.CopyToAsync(stream);
                        }

                        member.PhotoPath = "/uploads/members/" + uniqueFileName;
                    }
                    else
                    {
                        member.PhotoPath = existingMember.PhotoPath;
                    }

                    // 如果表單有填新的 Password，則重新 hash
                    if (!string.IsNullOrEmpty(member.Password))
                    {
                        var hasher = new PasswordHasher<Member>();
                        member.PasswordHash = hasher.HashPassword(member, member.Password);
                    }
                    else
                    {
                        // 保留原本的 hash
                        member.PasswordHash = existingMember.PasswordHash;
                    }

                    // 检查 Email 是否已存在
                    if (string.IsNullOrWhiteSpace(member.Email) || !new EmailAddressAttribute().IsValid(member.Email))
                    {
                        ModelState.AddModelError("Email", "Invalid Email format");
                        return View(member);
                    }

                    var normalizedEmail = member.Email.Trim().ToLowerInvariant();
                    if (await _context.Members.AnyAsync(m => m.Email != null && m.Email.Trim().ToLower() == normalizedEmail && m.Id != member.Id))
                    {
                        ModelState.AddModelError("Email", "This email is already registered.");
                        return View(member);
                    }

                    member.IsEmailConfirmed = existingMember.IsEmailConfirmed;
                    member.EmailConfirmToken = existingMember.EmailConfirmToken;
                    member.EmailConfirmTokenExpiry = existingMember.EmailConfirmTokenExpiry;

                    _context.Update(member);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Members.Any(e => e.Id == member.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(member);
        }

        // GET: Members/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var member = await _context.Members.FirstOrDefaultAsync(m => m.Id == id);
            if (member == null) return NotFound();
            return View(member);
        }

        // POST: Members/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var member = await _context.Members.FirstOrDefaultAsync(m => m.Id == id);

            if (member != null)
            {
                // 找到所有该 Member 的 VoucherId
                var memberVoucherIds = await _context.MemberVouchers
                    .Where(mv => mv.MemberId == id)
                    .Select(mv => mv.Id)
                    .ToListAsync();

                // 清理 OrderItem 里的外键
                var orderItems = await _context.OrderItems
                    .Where(oi => oi.MemberVoucherId != null && memberVoucherIds.Contains(oi.MemberVoucherId.Value))
                    .ToListAsync();

                foreach (var oi in orderItems)
                {
                    oi.MemberVoucherId = null;
                }

                // 删除 MemberVoucher
                var vouchers = _context.MemberVouchers.Where(mv => mv.MemberId == id);
                _context.MemberVouchers.RemoveRange(vouchers);

                // 删除 Member
                _context.Members.Remove(member);

                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }



        private bool MemberExists(int id)
        {
            return _context.Members.Any(e => e.Id == id);
        }

        //UserPage 那里用的
        public async Task<IActionResult> UserPage()
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

            // 获取最新的QR token
            var latestToken = await _context.QrLoginTokens
                .Where(t => t.MemberId == member.Id && t.Expiry > DateTime.UtcNow && !t.IsUsed)
                .OrderByDescending(t => t.Expiry)
                .FirstOrDefaultAsync();

            ViewBag.LatestToken = latestToken;

            return View(member);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RenewQrToken()
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

            try
            {
                // 使旧的token失效
                var oldTokens = await _context.QrLoginTokens
                    .Where(t => t.MemberId == member.Id && !t.IsUsed)
                    .ToListAsync();

                foreach (var token in oldTokens)
                {
                    token.IsUsed = true;
                }

                // 创建新的token
                var newToken = new QrLoginToken
                {
                    MemberId = member.Id,
                    Token = Guid.NewGuid().ToString("N"),
                    Expiry = DateTime.UtcNow.AddYears(1),
                    IsUsed = false
                };

                _context.QrLoginTokens.Add(newToken);
                await _context.SaveChangesAsync();

                // 生成QR码
                var qrPayload = newToken.Token;
                var pngBytes = GenerateQrPng(qrPayload);

                // 发送邮件
                var html = $@"<p>Hi {member.Username},</p>
                      <p>Your new QR login code is attached. Use it to log in to your account.</p>
                      <p>This QR code will expire on: {newToken.Expiry.ToString("yyyy-MM-dd HH:mm")}</p>";

                await SendEmailAsync(member.Email, "Your New QR Login Code", html, pngBytes, "qr.png");

                TempData["Message"] = "A new QR code has been sent to your email.";
            }
            catch (Exception ex)
            {
                TempData["Message"] = "Error generating new QR code. Please try again later.";
            }

            return RedirectToAction("UserPage");
        }

        private byte[] GenerateQrPng(string text)
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
            var pngWriter = new PngByteQRCode(qrData);
            return pngWriter.GetGraphic(20);
        }

        private async Task SendEmailAsync(string to, string subject, string htmlBody, byte[]? attachment = null, string? attachmentName = null)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                _config["Smtp:DisplayName"] ?? "X Burger",
                _config["Smtp:From"]
            ));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            if (attachment != null && attachmentName != null)
            {
                bodyBuilder.Attachments.Add(attachmentName, attachment);
            }
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





        //User Profile 那里用的
        public async Task<IActionResult> Profile()
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

            return View(member);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(Member member, IFormFile? Photo, string? currentPassword, string? newPassword, string? confirmPassword)
        {

            if (string.IsNullOrWhiteSpace(member.Username))
            {
                ModelState.AddModelError(string.Empty, "Username cant be empty");
            }


            
            if (!ModelState.IsValid)
            {
                // 若需要在返回 view 时显示现有头像
                var tmp = await _context.Members.FindAsync(member.Id);
                if (tmp != null) member.PhotoPath = tmp.PhotoPath;
                return View(member);
            }

            var existing = await _context.Members.FindAsync(member.Id);
            if (existing == null) return NotFound();

            // 更新基础资料
            existing.Username = member.Username;
            existing.Email = member.Email;

            
            if (!string.IsNullOrEmpty(currentPassword) || !string.IsNullOrEmpty(newPassword) || !string.IsNullOrEmpty(confirmPassword))
            {
                // 必须三项都填
                if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword) || string.IsNullOrEmpty(confirmPassword))
                {
                    ModelState.AddModelError(string.Empty, "To change password, please fill Current Password, New Password and Confirm Password.");
                    member.PhotoPath = existing.PhotoPath;
                    return View(member);
                }

                var hasher = new PasswordHasher<Member>();
                var verify = hasher.VerifyHashedPassword(existing, existing.PasswordHash, currentPassword);
                if (verify == PasswordVerificationResult.Failed)
                {
                    ModelState.AddModelError("currentPassword", "Current password is incorrect.");
                    member.PhotoPath = existing.PhotoPath;
                    return View(member);
                }

                // 新密长度检查
                if (newPassword.Length < 8)
                {
                    ModelState.AddModelError("newPassword", "New password must be at least 8 characters long.");
                    member.PhotoPath = existing.PhotoPath;
                    return View(member);
                }

                // 新密与确认必须相同
                if (newPassword != confirmPassword)
                {
                    ModelState.AddModelError("confirmPassword", "New password and Confirm password do not match.");
                    member.PhotoPath = existing.PhotoPath;
                    return View(member);
                }

                // 全部通过 -> hash 并保存
                existing.PasswordHash = hasher.HashPassword(existing, newPassword);
            }
            

            // 头像上传（若有上传则替换）
            if (Photo != null && Photo.Length > 0)
            {
                // 删除旧图
                if (!string.IsNullOrEmpty(existing.PhotoPath)
    && existing.PhotoPath.StartsWith("/uploads/members/"))
                {
                    var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", existing.PhotoPath.TrimStart('/'));
                    if (System.IO.File.Exists(oldFilePath)) System.IO.File.Delete(oldFilePath);
                }

                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "members");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(Photo.FileName);
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await Photo.CopyToAsync(stream);
                }

                existing.PhotoPath = "/uploads/members/" + uniqueFileName;
                HttpContext.Session.SetString("MemberPhoto", existing.PhotoPath); 
            }

            await _context.SaveChangesAsync();

            // 同步更新 Session
            HttpContext.Session.SetString("MemberUsername", existing.Username);
            HttpContext.Session.SetString("MemberEmail", existing.Email);

            TempData["Success"] = "Profile updated successfully!";
            return RedirectToAction("Profile");
        }


        public async Task<IActionResult> MyVoucher()
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

            var memberVouchers = await _context.MemberVouchers
                .Include(mv => mv.Voucher)
                .ThenInclude(v => v.Product)
                .Where(mv => mv.MemberId == member.Id && mv.ExpiryDate > DateTime.Now && !mv.IsUsed)
                .ToListAsync();

            return View(memberVouchers);
        }

        public async Task<IActionResult> RedeemVoucher()
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

            var vouchers = await _context.Vouchers
                .Include(v => v.Product)
                .Where(v => v.RedeemedCount < v.Limit)
                .ToListAsync();

            ViewBag.Member = member;

            return View(vouchers);
        }

        [HttpPost]
        public async Task<IActionResult> Redeem(int voucherId)
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

            var voucher = await _context.Vouchers.FindAsync(voucherId);
            if (voucher == null)
            {
                return NotFound();
            }

            // 检查领取次数
            if (voucher.RedeemedCount >= voucher.Limit)
            {
                TempData["Message"] = "This voucher is out of stock.";
                return RedirectToAction("RedeemVoucher");
            }

            // 检查用户积分
            if (member.Point < voucher.PointNeeded)
            {
                TempData["Message"] = "You don't have enough points.";
                return RedirectToAction("RedeemVoucher");
            }

            // 扣除积分
            member.Point -= voucher.PointNeeded;

            // 增加Voucher的领取次数
            voucher.RedeemedCount++;

            // 创建MemberVoucher
            var memberVoucher = new MemberVoucher
            {
                MemberId = member.Id,
                VoucherId = voucher.Id,
                RedeemedDate = DateTime.Now,
                ExpiryDate = DateTime.Now.AddDays(voucher.ValidDay),
                IsUsed = false
            };

            _context.MemberVouchers.Add(memberVoucher);
            await _context.SaveChangesAsync();

            TempData["Message"] = "Voucher redeemed successfully!";
            return RedirectToAction("MyVoucher");
        }

        [HttpGet]
        public IActionResult GetAvailableVouchers(int productId)
        {
            var email = HttpContext.Session.GetString("MemberEmail");
            if (string.IsNullOrEmpty(email))
            {
                return Json(new { });
            }

            var member = _context.Members.FirstOrDefault(m => m.Email == email);
            if (member == null)
            {
                return Json(new { });
            }

            var availableVouchers = _context.MemberVouchers
                .Include(mv => mv.Voucher)
                .Where(mv => mv.MemberId == member.Id &&
                            !mv.IsUsed &&
                            mv.ExpiryDate > DateTime.Now &&
                            mv.Voucher.ProductId == productId)
                .Select(mv => new
                {
                    memberVoucherId = mv.Id,
                    voucherName = mv.Voucher.Name,
                    voucherDetail = mv.Voucher.Detail,
                    discountedPrice = mv.Voucher.DiscountedPrice
                })
                .ToList();

            return Json(availableVouchers);
        }

        [HttpGet]
        public IActionResult CheckVoucherUsed(int id)
        {
            var memberVoucher = _context.MemberVouchers.Find(id);
            if (memberVoucher == null)
            {
                return Json(new { isUsed = true });
            }

            return Json(new { isUsed = memberVoucher.IsUsed });
        }

        [HttpGet]
        public IActionResult GetVoucherProduct(int id)
        {
            var memberVoucher = _context.MemberVouchers
                .Include(mv => mv.Voucher)
                .FirstOrDefault(mv => mv.Id == id);

            if (memberVoucher == null || memberVoucher.Voucher == null)
            {
                return Json(new { });
            }

            return Json(new { productId = memberVoucher.Voucher.ProductId });
        }


        // GET: Members/Search
        [HttpGet]
        public async Task<IActionResult> Search(string field, string keyword, int page = 1, int pageSize = 10)
        {
            var query = _context.Members.AsQueryable();

            if (!string.IsNullOrEmpty(field) && !string.IsNullOrEmpty(keyword))
            {
                switch (field)
                {
                    case "id":
                        if (!string.IsNullOrEmpty(keyword))
                        {
                            query = query.Where(m => m.Id.ToString().Contains(keyword));
                        }
                        break;

                    case "email":
                        
                        query = query.Where(m => m.Email != null && m.Email.Contains(keyword));
                        break;

                    default:
                        break;
                }
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            if (totalPages <= 1) totalPages = 1; // 确保至少显示 1 页

            var members = await query
                .OrderBy(m => m.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.TotalPages = totalPages;
            ViewBag.CurrentPage = page;

            return PartialView("_MembersTable", members);
        }



    }
}

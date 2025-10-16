// Controllers/LoginController.cs
using Demo.Models;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MimeKit;
using static Org.BouncyCastle.Math.EC.ECCurve;

namespace Demo.Controllers
{
    public class LoginController : Controller
    {
        private readonly DB _context;
        private readonly IConfiguration _config;

        public LoginController(DB context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // GET: Login
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(string identifier, string password)
        {
            if (string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(password))
            {
                ViewBag.Error = "Please enter both username/email and password.";
                return View();
            }

            var admin = await _context.Admins.FirstOrDefaultAsync(a => EF.Functions.Collate(a.Username, "SQL_Latin1_General_CP1_CS_AS") == identifier);

            if (admin != null)
            {
                var hasher = new PasswordHasher<Admin>();
                var result = hasher.VerifyHashedPassword(admin, admin.PasswordHash, password);
                if (result == PasswordVerificationResult.Success || result == PasswordVerificationResult.SuccessRehashNeeded)
                {
                    
                    if (result == PasswordVerificationResult.SuccessRehashNeeded)
                    {
                        admin.PasswordHash = hasher.HashPassword(admin, password);
                        _context.Update(admin);
                        await _context.SaveChangesAsync();
                    }

                    HttpContext.Session.SetString("AdminUsername", admin.Username);
                    HttpContext.Session.SetInt32("AdminId", admin.Id);
                    HttpContext.Session.SetString("IsSuperAdmin", admin.IsSuperAdmin.ToString());

                    // 发送给 SuperAdmin 的通知
                    try
                    {
                        
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";


                        var superAdmins = await _context.Admins
                            .Where(a => a.IsSuperAdmin && a.Email != null)
                            .ToListAsync();

                        if (superAdmins != null && superAdmins.Any())
                        {
                            var malaysiaTime = TimeZoneInfo.ConvertTimeFromUtc(
                                DateTime.UtcNow,
                                TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time")
                            );

                            var nowMy = malaysiaTime.ToString("yyyy-MM-dd HH:mm:ss 'MYT'");

                            var subject = $"[X Burger] Admin Login Notification - {admin.Username ?? ("ID " + admin.Id)}";
                            var htmlBody = $@"
                        <p>Hi,</p>
                        <p>The admin <strong>{admin.Username ?? "(no username)"} (ID: {admin.Id})</strong> just logged in.</p>
                        <ul>
                            <li><strong>Time :</strong> {nowMy}</li>
                            <li><strong>IP   :</strong> {ipAddress}</li>
                        </ul>
                        <p>If this was unexpected, please review admin activity.</p>
                    ";

                            foreach (var sa in superAdmins)
                            {
                                if (string.IsNullOrWhiteSpace(sa.Email)) continue;
                                try
                                {
                                    
                                    await SendEmailAsync(sa.Email, subject, htmlBody);
                                }
                                catch
                                {
                                    
                                }
                            }
                        }
                    }
                    catch
                    {
                        
                    }

                    return RedirectToAction("HomePage", "Home");
                }
            }

            // 再嘗試作為 Member 登入
            var member = await _context.Members.FirstOrDefaultAsync(m => m.Email == identifier);
            if (member != null)
            {
                var hasher = new PasswordHasher<Member>();
                var verify = hasher.VerifyHashedPassword(member, member.PasswordHash, password);
                if (verify == PasswordVerificationResult.Success)
                {
                    if (!member.IsEmailConfirmed)
                    {
                        ViewBag.Error = "Please confirm your email before logging in.";
                        return View();
                    }

                    HttpContext.Session.SetString("MemberEmail", member.Email);
                    HttpContext.Session.SetString("MemberUsername", member.Username ?? "");
                    HttpContext.Session.SetString("MemberPhoto", member.PhotoPath ?? "/images/no-image.png");
                    return RedirectToAction("Index", "Home");
                }
            }

            ViewBag.Error = "Invalid login attempt. Please check your credentials.";
            return View();
        }

        // POST: QR code login
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> QRCodeLogin(string token)
        {
            if (string.IsNullOrEmpty(token))
                return Json(new { success = false, message = "No token provided" });

            var qr = await _context.QrLoginTokens.Include(q => q.Member)
                        .FirstOrDefaultAsync(q => q.Token == token && q.Expiry > DateTime.UtcNow && q.Member != null && !q.IsUsed);

            if (qr == null)
            {
                return Json(new { success = false, message = "Invalid or expired token." });
            }

            // true是一次性，不要的话就删掉这里
            //qr.IsUsed = true;
            await _context.SaveChangesAsync();

            HttpContext.Session.SetString("MemberEmail", qr.Member.Email ?? "");
            HttpContext.Session.SetString("MemberUsername", qr.Member.Username ?? "");
            HttpContext.Session.SetString("MemberPhoto", qr.Member.PhotoPath ?? "/images/no-image.png");
            HttpContext.Session.SetInt32("MemberId", qr.Member.Id);

            return Json(new { success = true });
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }


        // GET: ForgetPassword
        public IActionResult ForgetPassword()
        {
            return View();
        }

        // POST: ForgetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgetPassword(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                ViewBag.Error = "Please enter your email address.";
                return View();
            }

            // 检查是否存在该邮箱的用户（Member或Admin）
            var member = await _context.Members.FirstOrDefaultAsync(m => m.Email == email);
            var admin = await _context.Admins.FirstOrDefaultAsync(a => a.Email == email);

            if (member == null && admin == null)
            {
                ViewBag.Message = "If the email exists in our system, a password reset link has been sent.";
                return View();
            }

            // 生成重置令牌
            var resetToken = Guid.NewGuid().ToString();
            var expiry = DateTime.UtcNow.AddHours(1); // 1小时后过期

            if (member != null)
            {
                member.PasswordResetToken = resetToken;
                member.PasswordResetTokenExpiry = expiry;
                _context.Update(member);
            }
            else if (admin != null)
            {

                ViewBag.Message = "Admin password reset is not supported yet.";
                return View();
            }

            await _context.SaveChangesAsync();

            // 发送重置邮件
            var resetLink = Url.Action("ResetPassword", "Login", new { token = resetToken }, Request.Scheme);
            var html = $@"<p>You requested a password reset. Click <a href='{resetLink}'>here</a> to reset your password.</p>
                 <p>This link will expire in 1 hour.</p>";

            try
            {
                await SendEmailAsync(email, "Password Reset Request", html);
                ViewBag.Message = "If the email exists in our system, a password reset link has been sent.";
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Failed to send email. Please try again later.";
            }

            return View();
        }

        // GET: ResetPassword
        public IActionResult ResetPassword(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return RedirectToAction("Index");
            }

            // 验证令牌有效性
            var member = _context.Members.FirstOrDefault(m => m.PasswordResetToken == token &&
                                                             m.PasswordResetTokenExpiry > DateTime.UtcNow);

            if (member == null)
            {
                ViewBag.Error = "Invalid or expired reset token.";
                return View();
            }

            ViewBag.Token = token;
            return View();
        }

        // POST: ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string token, string newPassword, string confirmPassword)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(newPassword))
            {
                ViewBag.Error = "Invalid request.";
                return View();
            }

            if (newPassword != confirmPassword)
            {
                ViewBag.Error = "Passwords do not match.";
                ViewBag.Token = token;
                return View();
            }

            var member = await _context.Members.FirstOrDefaultAsync(m => m.PasswordResetToken == token &&
                                                                        m.PasswordResetTokenExpiry > DateTime.UtcNow);

            if (member == null)
            {
                ViewBag.Error = "Invalid or expired reset token.";
                return View();
            }

            // 更新密码
            var hasher = new PasswordHasher<Member>();
            member.PasswordHash = hasher.HashPassword(member, newPassword);
            member.PasswordResetToken = null;
            member.PasswordResetTokenExpiry = null;

            _context.Update(member);
            await _context.SaveChangesAsync();

            ViewBag.Success = "Password has been reset successfully. You can now login with your new password.";
            return View();
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

    }
}

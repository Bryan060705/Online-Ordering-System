// Controllers/AccountController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Demo.Models;
using Microsoft.AspNetCore.Identity;
using QRCoder;
using MimeKit;
using MailKit.Net.Smtp;

namespace Demo.Controllers
{
    public class AccountController : Controller
    {
        private readonly DB _context;
        private readonly IConfiguration _config;

        public AccountController(DB context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        // GET: /Account/Register
        public IActionResult Register()
        {
            return View();
        }

        // POST: /Account/Register
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string username, string email, string password, string confirmPassword)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirmPassword))
            {
                ViewBag.Error = "Please fill all fields.";
                return View();
            }

            // 确认密码是否一致
            if (password != confirmPassword)
            {
                ViewBag.Error = "Passwords do not match.";
                return View();
            }

            // 密码长度验证
            if (password.Length < 8)
            {
                ViewBag.Error = "Password must be at least 8 characters.";
                return View();
            }

            if (await _context.Members.AnyAsync(m => m.Email == email))
            {
                ViewBag.Error = "Email already used.";
                return View();
            }

            var member = new Member
            {
                Username = username,
                Email = email,
                Point = 0,
                PhotoPath = "/images/default.png",
                IsEmailConfirmed = false
            };

            // Hash password
            var hasher = new PasswordHasher<Member>();
            member.PasswordHash = hasher.HashPassword(member, password);

            // confirm token
            var token = Guid.NewGuid().ToString("N");
            member.EmailConfirmToken = token;
            member.EmailConfirmTokenExpiry = DateTime.UtcNow.AddHours(24);

            _context.Members.Add(member);
            await _context.SaveChangesAsync();

            // confirm email
            var confirmUrl = Url.Action("ConfirmEmail", "Account", new { token }, Request.Scheme);
            var html = $@"<p>Hi {username},</p>
            <p>Please confirm your email by clicking the link below:</p>
            <p><a href=""{confirmUrl}"">Confirm email</a></p>
            <p>If you did not register, ignore this email.</p>";

            await SendEmailAsync(email, "Confirm your account", html);

            return View("RegisterConfirmation");
        }


        public async Task<IActionResult> ConfirmEmail(string token)
        {
            if (string.IsNullOrEmpty(token)) return View("ConfirmEmail", model: "Invalid token");

            var member = await _context.Members.FirstOrDefaultAsync(m => m.EmailConfirmToken == token && m.EmailConfirmTokenExpiry > DateTime.UtcNow);
            if (member == null) return View("ConfirmEmail", model: "Invalid or expired token.");

            member.IsEmailConfirmed = true;
            member.EmailConfirmToken = null;
            member.EmailConfirmTokenExpiry = null;

            // 產生一個 QR login token 並儲存
            var qrToken = new QrLoginToken
            {
                MemberId = member.Id,
                Token = Guid.NewGuid().ToString("N"),
                Expiry = DateTime.UtcNow.AddYears(1), //时间
                IsUsed = false
            };

            _context.QrLoginTokens.Add(qrToken);

            await _context.SaveChangesAsync();

            // 把Token放去QR
            var qrPayload = qrToken.Token;
            var png = GenerateQrPng(qrPayload);

            var html = $@"<p>Hi {member.Username},</p>
                          <p>Your account has been confirmed. Attached is your QR code you can use to login (open app -> Scan).</p>";

            await SendEmailAsync(member.Email, "Your QR Login Code", html, png, "qr.png");

            return View("ConfirmEmail", model: "Your email is confirmed. We sent QR code to your email.");
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

            using var client = new MailKit.Net.Smtp.SmtpClient();
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

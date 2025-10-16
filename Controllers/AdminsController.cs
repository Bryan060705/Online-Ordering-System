using Demo.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Demo.Controllers
{
    public class AdminsController : Controller
    {
        private readonly DB _context;

        public AdminsController(DB context)
        {
            _context = context;
        }

        // GET: Admins
        public async Task<IActionResult> Index()
        {
            return View(await _context.Admins.ToListAsync());
        }

        // GET: Admins/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var admin = await _context.Admins
                .FirstOrDefaultAsync(m => m.Id == id);
            if (admin == null)
            {
                return NotFound();
            }

            return View(admin);
        }

        // GET: Admins/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Admins/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Username,Password,Email,IsSuperAdmin")] Admin admin, IFormFile? Photo)
        {
            // 如果是 SuperAdmin，Email 必填
            if (admin.IsSuperAdmin && string.IsNullOrWhiteSpace(admin.Email))
            {
                ModelState.AddModelError("Email", "Email is required for SuperAdmin.");
            }

            if (ModelState.IsValid)
            {
                if (!string.IsNullOrEmpty(admin.Password))
                {
                    var hasher = new PasswordHasher<Admin>();
                    admin.PasswordHash = hasher.HashPassword(admin, admin.Password);
                    admin.Password = null; // 清除输入字段
                }

                if (Photo != null && Photo.Length > 0)
                {
                    var fileName = Path.GetFileName(Photo.FileName);
                    var filePath = Path.Combine("wwwroot/uploads", fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await Photo.CopyToAsync(stream);
                    }

                    admin.PhotoPath = "/uploads/" + fileName;
                }
                else
                {
                    admin.PhotoPath = "/images/default.png";
                }

                _context.Add(admin);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            return View(admin);
        }



        // GET: Admins/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var admin = await _context.Admins.FindAsync(id);
            if (admin == null)
            {
                return NotFound();
            }
            return View(admin);
        }

        // POST: Admins/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Username,Password,Email,IsSuperAdmin")] Admin admin, IFormFile? Photo)
        {
            if (id != admin.Id)
            {
                return NotFound();
            }

            // SuperAdmin -> Email 必须要有
            if (admin.IsSuperAdmin && string.IsNullOrWhiteSpace(admin.Email))
            {
                ModelState.AddModelError("Email", "Email is required for SuperAdmin.");
            }

            if (!ModelState.IsValid)
            {
                return View(admin);
            }

            // 先从数据库取回现存实体（tracked 实体）
            var existing = await _context.Admins.FirstOrDefaultAsync(a => a.Id == id);
            if (existing == null) return NotFound();

            try
            {
                // 处理照片上传：只有在上传新照片时才替换并删除旧文件（如果旧文件是上传目录下）
                if (Photo != null && Photo.Length > 0)
                {
                    // 删除旧图（仅删除用户上传的文件，不删除公用默认图片）
                    if (!string.IsNullOrEmpty(existing.PhotoPath) && existing.PhotoPath.StartsWith("/uploads/"))
                    {
                        try
                        {
                            var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", existing.PhotoPath.TrimStart('/'));
                            if (System.IO.File.Exists(oldFilePath)) System.IO.File.Delete(oldFilePath);
                        }
                        catch
                        {
                            // 忽略文件删除问题（不应阻止 DB 更新），可记录日志
                        }
                    }

                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                    var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(Photo.FileName);
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await Photo.CopyToAsync(stream);
                    }

                    existing.PhotoPath = "/uploads/" + uniqueFileName;
                }
                

                
                if (!string.IsNullOrEmpty(admin.Password))
                {
                    var hasher = new PasswordHasher<Admin>();
                    existing.PasswordHash = hasher.HashPassword(existing, admin.Password);
                }

                
                existing.Username = admin.Username;
                existing.Email = admin.Email;
                existing.IsSuperAdmin = admin.IsSuperAdmin;

                _context.Update(existing);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Admins.Any(e => e.Id == admin.Id))
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




        // GET: Admins/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var admin = await _context.Admins
                .FirstOrDefaultAsync(m => m.Id == id);
            if (admin == null)
            {
                return NotFound();
            }

            return View(admin);
        }

        // POST: Admins/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var admin = await _context.Admins.FindAsync(id);
            if (admin != null)
            {
                _context.Admins.Remove(admin);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool AdminExists(int id)
        {
            return _context.Admins.Any(e => e.Id == id);
        }

        // GET: /Account/ForgetPassword
        public IActionResult ForgetPassword()
        {
            return View("~/Views/Login/ForgetPassword.cshtml");
        }

        // POST: /Account/ForgetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgetPassword(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                ViewBag.Error = "Please enter your email address.";
                return View("~/Views/Login/ForgetPassword.cshtml");
            }

            // 先找 Member
            var member = await _context.Members.FirstOrDefaultAsync(m => m.Email == email);
            if (member != null)
            {
                string token = Guid.NewGuid().ToString();
                var resetLink = Url.Action("ResetPassword", "Account",
                    new { email = member.Email, token = token, userType = "Member" }, Request.Scheme);

                await SendResetEmail(member.Email, member.Username, resetLink);
                ViewBag.Message = "If the email exists in our system, a password reset link has been sent.";
                return View("~/Views/Login/ForgetPassword.cshtml");
            }

            // 再找 Admin
            var admin = await _context.Admins.FirstOrDefaultAsync(a => a.Email == email);
            if (admin != null)
            {
                string token = Guid.NewGuid().ToString();
                var resetLink = Url.Action("ResetPassword", "Account",
                    new { email = admin.Email, token = token, userType = "Admin" }, Request.Scheme);

                await SendResetEmail(admin.Email, admin.Username, resetLink);
                ViewBag.Message = "If the email exists in our system, a password reset link has been sent.";
                return View("~/Views/Login/ForgetPassword.cshtml");
            }

            ViewBag.Error = "Invalid or expired reset token.";
            return View("~/Views/Login/ForgetPassword.cshtml");
        }

        // GET: /Account/ResetPassword
        public IActionResult ResetPassword(string email, string token, string userType)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(userType))
            {
                return RedirectToAction("Login", "Account");
            }

            ViewBag.Email = email;
            ViewBag.Token = token;
            ViewBag.UserType = userType;
            return View("~/Views/Login/ResetPassword.cshtml");
        }

        // POST: /Account/ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string email, string token, string newPassword, string confirmPassword, string userType)
        {
            if (newPassword != confirmPassword)
            {
                ViewBag.Error = "Passwords do not match.";
                return View("~/Views/Login/ResetPassword.cshtml");
            }

            if (userType == "Member")
            {
                var member = await _context.Members.FirstOrDefaultAsync(m => m.Email == email);
                if (member == null)
                {
                    ViewBag.Error = "Invalid or expired reset token.";
                    return View("~/Views/Login/ResetPassword.cshtml");
                }

                member.Password = newPassword; 
                _context.Update(member);
                await _context.SaveChangesAsync();

                ViewBag.Success = "Password has been reset successfully. You can now login with your new password.";
                return View("~/Views/Login/ResetPassword.cshtml");
            }
            else if (userType == "Admin")
            {
                var admin = await _context.Admins.FirstOrDefaultAsync(a => a.Email == email);
                if (admin == null)
                {
                    ViewBag.Error = "Invalid Admin";
                    return View("~/Views/Login/ResetPassword.cshtml");
                }

                admin.Password = newPassword; 
                _context.Update(admin);
                await _context.SaveChangesAsync();

                ViewBag.Success = "Password has been reset successfully. You can now login with your new password.";
                return View("~/Views/Login/ResetPassword.cshtml");
            }

            ViewBag.Error = "Invalid Admin";
            return View("~/Views/Login/ResetPassword.cshtml");
        }

        // 发送邮件方法
        private async Task SendResetEmail(string email, string username, string resetLink)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Admin", "your_email@example.com"));
            message.To.Add(new MailboxAddress(username, email));
            message.Subject = "Reset Password";
            message.Body = new TextPart("plain")
            {
                Text = $"Please click the link below to reset your password: {resetLink}"
            };

            using (var client = new MailKit.Net.Smtp.SmtpClient())
            {
                await client.ConnectAsync("smtp.gmail.com", 587, MailKit.Security.SecureSocketOptions.StartTls);
                await client.AuthenticateAsync("your_email@example.com", "your_email_password");
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
        }




    }
}

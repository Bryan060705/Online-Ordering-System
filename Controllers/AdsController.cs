// 文件: Controllers/AdsController.cs
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Demo.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Demo.Controllers
{
    public class AdsController : Controller
    {
        private readonly DB _context;
        private readonly IWebHostEnvironment _env;

        public AdsController(DB context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // 列表
        public async Task<IActionResult> Index()
        {
            var ads = await _context.Ads
                .Include(a => a.Product)
                .OrderBy(a => a.DisplayOrder)
                .ToListAsync();
            return View(ads);
        }

        // Create - GET
        public IActionResult Create()
        {
            ViewBag.Products = _context.Products.Where(p => p.IsActive).ToList();
            return View();
        }

        // Create - POST 
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Ad ad, IFormFile imageFile)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Products = _context.Products.Where(p => p.IsActive).ToList();
                return View(ad);
            }

            // 如果填了 URL 就清掉 ProductId
            if (!string.IsNullOrWhiteSpace(ad.Url))
            {
                ad.ProductId = null;
            }

            if (imageFile != null && imageFile.Length > 0)
            {
                var uploads = Path.Combine(_env.WebRootPath, "images", "ads");
                if (!Directory.Exists(uploads)) Directory.CreateDirectory(uploads);

                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
                var path = Path.Combine(uploads, fileName);
                using var stream = new FileStream(path, FileMode.Create);
                await imageFile.CopyToAsync(stream);
                ad.ImagePath = "/images/ads/" + fileName;
            }

            _context.Ads.Add(ad);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // Edit - GET
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var ad = await _context.Ads.FindAsync(id);
            if (ad == null) return NotFound();

            ViewBag.Products = _context.Products.Where(p => p.IsActive).ToList();
            return View(ad);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Ad ad, IFormFile? newImage)
        {
            if (id != ad.Id)
            {
                return BadRequest();
            }

            var existingAd = await _context.Ads.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id); 
            if (existingAd == null) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Products = _context.Products.Where(p => p.IsActive).ToList();
                ad.ImagePath = existingAd.ImagePath; 
                return View(ad);
            }

            var adToUpdate = await _context.Ads.FindAsync(id);
            if (adToUpdate == null) return NotFound(); 

            adToUpdate.Title = ad.Title?.Trim() ?? "";
            adToUpdate.ProductId = ad.ProductId; 
            adToUpdate.Url = string.IsNullOrWhiteSpace(ad.Url) ? null : ad.Url.Trim();
            adToUpdate.IsActive = ad.IsActive;
            adToUpdate.DisplayOrder = ad.DisplayOrder;

            // 如果有 Url 就清 ProductId；没 Url 就清 Url
            if (!string.IsNullOrEmpty(adToUpdate.Url))
            {
                adToUpdate.ProductId = null;
            }
            else
            {
                adToUpdate.Url = null;
            }

            if (newImage != null && newImage.Length > 0)
            {
                var uploads = Path.Combine(_env.WebRootPath, "images", "ads");
                if (!Directory.Exists(uploads)) Directory.CreateDirectory(uploads);

                if (!string.IsNullOrEmpty(adToUpdate.ImagePath))
                {
                    var oldPath = Path.Combine(_env.WebRootPath,
                        adToUpdate.ImagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(oldPath))
                    {
                        try { System.IO.File.Delete(oldPath); }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error deleting old image: {ex.Message}");
                        }
                    }
                }

                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(newImage.FileName);
                var path = Path.Combine(uploads, fileName);
                using var stream = new FileStream(path, FileMode.Create);
                await newImage.CopyToAsync(stream);
                adToUpdate.ImagePath = "/images/ads/" + fileName;
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                ModelState.AddModelError("", "Save Fail：" + ex.Message);
                ViewBag.Products = _context.Products.Where(p => p.IsActive).ToList();
                return View(adToUpdate);
            }

            return RedirectToAction(nameof(Index));
        }

        // Delete - GET
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var ad = await _context.Ads.FindAsync(id);
            if (ad == null) return NotFound();
            return View(ad);
        }

        // Delete - POST
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var ad = await _context.Ads.FindAsync(id);
            if (ad != null)
            {
                if (!string.IsNullOrEmpty(ad.ImagePath))
                {
                    var oldPath = Path.Combine(_env.WebRootPath,
                        ad.ImagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(oldPath))
                    {
                        try { System.IO.File.Delete(oldPath); } catch {}
                    }
                }
                _context.Ads.Remove(ad);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }


        [HttpPost]
        public async Task<IActionResult> ToggleActive(int id, bool isActive)
        {
            var ad = await _context.Ads.FindAsync(id);
            if (ad == null) return NotFound();
            ad.IsActive = isActive;
            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}
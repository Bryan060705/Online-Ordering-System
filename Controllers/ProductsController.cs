using Demo.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using X.PagedList;

namespace Demo.Controllers
{
    public class ProductsController : Controller
    {
        private readonly DB _context;
        private readonly IWebHostEnvironment _env;

        public ProductsController(DB context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // GET: Products
        public async Task<IActionResult> Index()
        {
            return View(await _context.Products
                .Include(p => p.Images)  
                .ToListAsync());
        }


        // GET: Products/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var product = await _context.Products
                .Include(p => p.Images)   
                .FirstOrDefaultAsync(m => m.Id == id);
            if (product == null)
            {
                return NotFound();
            }

            return View(product);
        }

        // GET: Products/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Products/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Product product, List<IFormFile> imageFiles)
        {
            if (ModelState.IsValid)
            {
                product.Images = new List<ProductImage>();

                if (imageFiles != null && imageFiles.Count > 0)
                {
                    foreach (var file in imageFiles)
                    {
                        if (file.Length > 0)
                        {
                            var fileName = Path.GetFileName(file.FileName);
                            var filePath = Path.Combine("wwwroot/images", fileName);

                            using (var stream = new FileStream(filePath, FileMode.Create))
                            {
                                await file.CopyToAsync(stream);
                            }

                            product.Images.Add(new ProductImage
                            {
                                ImagePath = "/images/" + fileName
                            });
                        }
                    }
                }

                _context.Add(product);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(product);
        }





        // GET: Products/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var product = await _context.Products
                .Include(p => p.Images)   
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound();
            }
            return View(product);
        }


        // POST: Products/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Product product, List<IFormFile> newImages, List<int> deleteImages)
        {
            if (id != product.Id) return NotFound();
            if (!ModelState.IsValid) return View(product);

            var existingProduct = await _context.Products
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (existingProduct == null) return NotFound();

            existingProduct.Name = product.Name;
            existingProduct.Price = product.Price;
            existingProduct.IsActive = product.IsActive; 

            // 删除旧图片
            if (deleteImages != null && deleteImages.Count > 0)
            {
                var imagesToRemove = existingProduct.Images
                    .Where(i => deleteImages.Contains(i.Id))
                    .ToList();

                _context.ProductImages.RemoveRange(imagesToRemove);
            }

            // 添加新图片
            if (newImages != null && newImages.Count > 0)
            {
                var allowedTypes = new[] { ".jpg", ".jpeg", ".png", ".gif" };
                foreach (var file in newImages)
                {
                    if (file.Length > 0)
                    {
                        var extension = Path.GetExtension(file.FileName).ToLower();
                        if (!allowedTypes.Contains(extension)) continue;

                        var fileName = Path.GetFileName(file.FileName);
                        var filePath = Path.Combine("wwwroot/images", fileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await file.CopyToAsync(stream);
                        }

                        existingProduct.Images.Add(new ProductImage
                        {
                            ImagePath = "/images/" + fileName
                        });
                    }
                }
            }

            _context.Update(existingProduct);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }






        // GET: Products/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var product = await _context.Products
                .Include(p => p.Images)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (product == null) return NotFound();

            return View(product);
        }


        // POST: Products/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            // 先把 product + images 一起拿出来
            var product = await _context.Products
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound();
            }

            // 删除磁盘上的图片文件（如果存在）
            if (product.Images != null && product.Images.Any())
            {
                foreach (var img in product.Images)
                {
                    try
                    {
                        // 假设 ImagePath 存的是 "/images/filename.jpg"
                        var fileName = Path.GetFileName(img.ImagePath);
                        var filePath = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "images", fileName);
                        if (System.IO.File.Exists(filePath))
                        {
                            System.IO.File.Delete(filePath);
                        }
                    }
                    catch
                    {
                        
                    }
                }

                // 删除 productImages 表里的记录
                _context.ProductImages.RemoveRange(product.Images);
            }

            // 删除 product
            _context.Products.Remove(product);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        private bool ProductExists(int id)
        {
            return _context.Products.Any(e => e.Id == id);
        }

        //售卖状态
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id, bool isActive)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            product.IsActive = isActive;
            _context.Update(product);
            await _context.SaveChangesAsync();
            return Ok();
        }


        // Ads 获取资料
        [HttpGet]
        public async Task<IActionResult> GetProductJson(int id)
        {
            var product = await _context.Products
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null) return NotFound();

            return Json(new
            {
                id = product.Id,
                name = product.Name,
                price = product.Price,
                images = product.Images?.Select(i => i.ImagePath).ToList() ?? new List<string>()
            });
        }


        // GET: Products/Search
        public async Task<IActionResult> Search(string field, string name, decimal? minPrice, decimal? maxPrice, bool? active, int page = 1, int pageSize = 5)
        {
            var query = _context.Products
                .Include(p => p.Images)
                .AsQueryable();

            // 根据选择的字段来过滤
            switch (field)
            {
                case "name":
                    if (!string.IsNullOrEmpty(name))
                        query = query.Where(p => p.Name.Contains(name));
                    break;

                case "price":
                    if (minPrice.HasValue)
                        query = query.Where(p => p.Price >= minPrice.Value);
                    if (maxPrice.HasValue)
                        query = query.Where(p => p.Price <= maxPrice.Value);
                    break;

                case "active":
                    if (active.HasValue)
                        query = query.Where(p => p.IsActive == active.Value);
                    break;

                default:
                    break;
            }

            // 分页
            var totalCount = await query.CountAsync();
            var products = await query
                .OrderBy(p => p.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.CurrentPage = page;

            return PartialView("_ProductsTable", products); 
        }






    }
}

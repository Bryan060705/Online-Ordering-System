using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Demo.Models;

namespace Demo.Controllers
{
    public class HomeController : Controller
    {
        private readonly DB _context;

        public HomeController(DB context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string useVoucher = null)
        {
            // 只显示启用的产品
            var products = await _context.Products
                .Include(p => p.Images)
                .Where(p => p.IsActive)
                .ToListAsync();

            // 广告
            var ads = await _context.Ads
                .Where(a => a.IsActive)
                .OrderBy(a => a.DisplayOrder)
                .ToListAsync();

            var vm = new HomeIndexViewModel
            {
                Products = products,
                Ads = ads
            };

            // 如果有Voucher使用请求，保存到Session
            if (!string.IsNullOrEmpty(useVoucher) && int.TryParse(useVoucher, out int voucherId))
            {
                HttpContext.Session.SetInt32("UseVoucher", voucherId);
            }

            return View(vm);
        }

        [HttpPost]
        public IActionResult SetDiningOption(string option)
        {
            if (option == "DineIn" || option == "TakeAway")
            {
                HttpContext.Session.SetString("DiningOption", option);
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Invalid option" });
        }

        [HttpGet]
        public IActionResult CheckDiningOption()
        {
            var option = HttpContext.Session.GetString("DiningOption");
            return Json(new { hasOption = !string.IsNullOrEmpty(option), option });
        }

        [HttpPost]
        public IActionResult ClearDiningOption()
        {
            HttpContext.Session.Remove("DiningOption");
            HttpContext.Session.Remove("DiningCapacity"); 
            return Json(new { success = true });
        }



        public IActionResult HomePage()
        {
            return View();
        }

        [HttpPost]
        public IActionResult ClearUseVoucher()
        {
            HttpContext.Session.Remove("UseVoucher");
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> CheckAndSetDiningOption(string option, int? capacity)
        {
            if (option == "DineIn")
            {
                if (!capacity.HasValue || capacity.Value < 1)
                    return Json(new { success = false, message = "Invalid capacity" });

                // 是否存在单张桌子可容纳该人数
                bool tableAvailable = await _context.Tables
                    .AnyAsync(t => t.IsAvailable && t.Capacity >= capacity.Value);

                if (tableAvailable)
                {
                    HttpContext.Session.SetString("DiningOption", "DineIn");
                    HttpContext.Session.SetInt32("DiningCapacity", capacity.Value);
                    return Json(new { success = true });
                }
                else
                {
                    return Json(new { success = false, message = "There are not enough seats available at the moment." });
                }
            }
            else if (option == "TakeAway")
            {
                HttpContext.Session.SetString("DiningOption", "TakeAway");
                HttpContext.Session.Remove("DiningCapacity");
                return Json(new { success = true });
            }

            return Json(new { success = false, message = "Invalid option" });
        }


    }
}

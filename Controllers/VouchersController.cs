using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Demo.Models;
using System.Linq;
using System.Threading.Tasks;

namespace Demo.Controllers
{
    public class VouchersController : Controller
    {
        private readonly DB _context;

        public VouchersController(DB context)
        {
            _context = context;
        }

        // GET: Vouchers
        public async Task<IActionResult> Index()
        {
            var vouchers = await _context.Vouchers.Include(v => v.Product).ToListAsync();
            return View(vouchers);
        }

        // GET: Vouchers/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var voucher = await _context.Vouchers
                .Include(v => v.Product)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (voucher == null)
            {
                return NotFound();
            }

            return View(voucher);
        }

        // GET: Vouchers/Create
        public IActionResult Create()
        {
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Name");
            return View();
        }

        // POST: Vouchers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Name,Detail,ValidDay,PointNeeded,Limit,ProductId,DiscountedPrice")] Voucher voucher)
        {
            if (ModelState.IsValid)
            {
                _context.Add(voucher);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Name", voucher.ProductId);
            return View(voucher);
        }

        // GET: Vouchers/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var voucher = await _context.Vouchers.FindAsync(id);
            if (voucher == null)
            {
                return NotFound();
            }
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Name", voucher.ProductId);
            return View(voucher);
        }

        // POST: Vouchers/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Detail,ValidDay,PointNeeded,Limit,ProductId,DiscountedPrice,RedeemedCount")] Voucher voucher)
        {
            if (id != voucher.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(voucher);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!VoucherExists(voucher.Id))
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
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Name", voucher.ProductId);
            return View(voucher);
        }

        // GET: Vouchers/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var voucher = await _context.Vouchers
                .Include(v => v.Product)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (voucher == null)
            {
                return NotFound();
            }

            return View(voucher);
        }

        // POST: Vouchers/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var voucher = await _context.Vouchers.FindAsync(id);
            if (voucher == null) return RedirectToAction(nameof(Index));

            
            var memberVoucherIds = await _context.MemberVouchers
                .Where(mv => mv.VoucherId == id)
                .Select(mv => mv.Id)
                .ToListAsync();

            if (memberVoucherIds.Any())
            {
                // 先把引用到这些 MemberVoucher 的 OrderItems 清理（设为 null）
                var orderItems = await _context.OrderItems
                    .Where(oi => oi.MemberVoucherId != null && memberVoucherIds.Contains(oi.MemberVoucherId.Value))
                    .ToListAsync();

                foreach (var oi in orderItems)
                {
                    oi.MemberVoucherId = null;
                    /*oi.IsVoucher = false;*/ // 把标记清掉
                }

                // 删除这些 MemberVoucher 记录
                var memberVouchers = _context.MemberVouchers.Where(mv => memberVoucherIds.Contains(mv.Id));
                _context.MemberVouchers.RemoveRange(memberVouchers);
            }

            // 最后删除 Voucher
            _context.Vouchers.Remove(voucher);

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }


        private bool VoucherExists(int id)
        {
            return _context.Vouchers.Any(e => e.Id == id);
        }
    }
}
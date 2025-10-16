using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Demo.Models;

namespace Demo.Controllers
{
    public class OrderItemsController : Controller
    {
        private readonly DB _context;

        public OrderItemsController(DB context)
        {
            _context = context;
        }

        // GET: OrderItems
        public async Task<IActionResult> Index()
        {
            var dB = _context.OrderItems.Include(o => o.Order).Include(o => o.Product);
            return View(await dB.ToListAsync());
        }

        // GET: OrderItems/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var orderItem = await _context.OrderItems
                .Include(o => o.Order)
                .Include(o => o.Product)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (orderItem == null)
            {
                return NotFound();
            }

            return View(orderItem);
        }

        // GET: OrderItems/Create
        public IActionResult Create()
        {
            ViewData["OrderId"] = new SelectList(_context.Orders, "Id", "Id");
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Name");
            return View();
        }

        // POST: OrderItems/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,OrderId,ProductId,Quantity,IsVoucher,MemberVoucherId")] OrderItem orderItem)
        {
            if (ModelState.IsValid)
            {
                var product = await _context.Products.FindAsync(orderItem.ProductId);
                if (product == null) return NotFound();

                decimal unitPrice;

                if (orderItem.IsVoucher && orderItem.MemberVoucherId.HasValue)
                {
                    var memberVoucher = await _context.MemberVouchers
                        .Include(mv => mv.Voucher)
                        .FirstOrDefaultAsync(mv => mv.Id == orderItem.MemberVoucherId);

                    if (memberVoucher == null) return NotFound("Voucher not found");

                    unitPrice = memberVoucher.Voucher.DiscountedPrice;
                }
                else
                {
                    unitPrice = product.Price;
                }

                orderItem.UnitPrice = unitPrice;
                orderItem.TotalPrice = unitPrice * orderItem.Quantity;

                _context.Add(orderItem);
                await _context.SaveChangesAsync();
                return RedirectToAction("Details", "Orders", new { id = orderItem.OrderId });
            }
            return View(orderItem);
        }


        // GET: OrderItems/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var orderItem = await _context.OrderItems.FindAsync(id);
            if (orderItem == null)
            {
                return NotFound();
            }
            ViewData["OrderId"] = new SelectList(_context.Orders, "Id", "Id", orderItem.OrderId);
            ViewData["ProductId"] = new SelectList(_context.Products, "Id", "Name", orderItem.ProductId);
            return View(orderItem);
        }

        // POST: OrderItems/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,OrderId,ProductId,Quantity,IsVoucher,MemberVoucherId")] OrderItem orderItem)
        {
            if (id != orderItem.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var product = await _context.Products.FindAsync(orderItem.ProductId);
                    if (product == null) return NotFound();

                    decimal unitPrice;

                    if (orderItem.IsVoucher && orderItem.MemberVoucherId.HasValue)
                    {
                        var memberVoucher = await _context.MemberVouchers
                            .Include(mv => mv.Voucher)
                            .FirstOrDefaultAsync(mv => mv.Id == orderItem.MemberVoucherId);

                        if (memberVoucher == null) return NotFound("Voucher not found");

                        unitPrice = memberVoucher.Voucher.DiscountedPrice;
                    }
                    else
                    {
                        unitPrice = product.Price;
                    }

                    orderItem.UnitPrice = unitPrice;
                    orderItem.TotalPrice = unitPrice * orderItem.Quantity;

                    _context.Update(orderItem);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.OrderItems.Any(e => e.Id == orderItem.Id))
                        return NotFound();
                    else
                        throw;
                }
                return RedirectToAction("Details", "Orders", new { id = orderItem.OrderId });
            }
            return View(orderItem);
        }


        // GET: OrderItems/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var orderItem = await _context.OrderItems
                .Include(o => o.Order)
                .Include(o => o.Product)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (orderItem == null)
            {
                return NotFound();
            }

            return View(orderItem);
        }

        // POST: OrderItems/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var orderItem = await _context.OrderItems.FindAsync(id);
            if (orderItem != null)
            {
                _context.OrderItems.Remove(orderItem);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool OrderItemExists(int id)
        {
            return _context.OrderItems.Any(e => e.Id == id);
        }
    }
}

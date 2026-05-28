using DYPStore.Data;
using DYPStore.Models;
using DYPStore.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace DYPStore.Controllers
{
    [Authorize]
    public class CartController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _um;
        public CartController(ApplicationDbContext db, UserManager<ApplicationUser> um) { _db = db; _um = um; }

        public async Task<IActionResult> Index()
        {
            var uid = _um.GetUserId(User)!;
            var items = await _db.CartItems.Include(c => c.Product).Where(c => c.UserId == uid).ToListAsync();
            var vm = new CartViewModel { Items = items.Select(i => new CartItemViewModel { CartItemId = i.Id, ProductId = i.ProductId, ProductName = i.Product.Name, Brand = i.Product.Brand, ImageUrl = i.Product.ImageUrl, UnitPrice = i.Product.Price, Quantity = i.Quantity, Stock = i.Product.Stock }).ToList() };
            return View(vm);
        }

        [HttpPost][ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(int productId, int quantity = 1)
        {
            var uid = _um.GetUserId(User)!;
            var product = await _db.Products.FindAsync(productId);
            if (product == null || product.Stock <= 0) { TempData["Error"] = "Producto no disponible."; return RedirectToAction("Details", "Products", new { id = productId }); }
            var existing = await _db.CartItems.FirstOrDefaultAsync(c => c.UserId == uid && c.ProductId == productId);
            if (existing != null) existing.Quantity = Math.Min(existing.Quantity + quantity, product.Stock);
            else _db.CartItems.Add(new CartItem { UserId = uid, ProductId = productId, Quantity = Math.Min(quantity, product.Stock) });
            await _db.SaveChangesAsync();
            TempData["Success"] = $"{product.Name} añadido al carrito.";
            return RedirectToAction("Index");
        }

        [HttpPost][ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateQuantity(int cartItemId, int quantity)
        {
            var uid = _um.GetUserId(User)!;
            var item = await _db.CartItems.Include(c => c.Product).FirstOrDefaultAsync(c => c.Id == cartItemId && c.UserId == uid);
            if (item != null) { if (quantity <= 0) _db.CartItems.Remove(item); else item.Quantity = Math.Min(quantity, item.Product.Stock); await _db.SaveChangesAsync(); }
            return RedirectToAction("Index");
        }

        [HttpPost][ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int cartItemId)
        {
            var uid = _um.GetUserId(User)!;
            var item = await _db.CartItems.FirstOrDefaultAsync(c => c.Id == cartItemId && c.UserId == uid);
            if (item != null) { _db.CartItems.Remove(item); await _db.SaveChangesAsync(); }
            return RedirectToAction("Index");
        }

        [HttpPost][ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout()
        {
            var uid = _um.GetUserId(User)!;
            var items = await _db.CartItems.Include(c => c.Product).Where(c => c.UserId == uid).ToListAsync();
            if (!items.Any()) { TempData["Error"] = "Tu carrito está vacío."; return RedirectToAction("Index"); }

            // Validar stock disponible antes de procesar (#9)
            var stockErrors = items
                .Where(i => i.Product.Stock < i.Quantity)
                .Select(i => $"{i.Product.Name} (disponible: {i.Product.Stock}, solicitado: {i.Quantity})");
            if (stockErrors.Any())
            {
                TempData["Error"] = $"Stock insuficiente para: {string.Join(", ", stockErrors)}. Ajusta las cantidades e intenta de nuevo.";
                return RedirectToAction("Index");
            }

            var order = new Order { UserId = uid, Status = OrderStatus.pending, Total = items.Sum(i => i.Product.Price * i.Quantity), Items = items.Select(i => new OrderItem { ProductId = i.ProductId, ProductName = i.Product.Name, UnitPrice = i.Product.Price, Quantity = i.Quantity }).ToList() };
            _db.Orders.Add(order);

            // Descontar el stock de cada producto (#4)
            foreach (var item in items)
                item.Product.Stock -= item.Quantity;

            _db.CartItems.RemoveRange(items);
            await _db.SaveChangesAsync();
            TempData["Success"] = "¡Pedido creado exitosamente! Gracias por tu compra.";
            return RedirectToAction("Index", "Orders");
        }
    }
}

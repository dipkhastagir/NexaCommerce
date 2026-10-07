using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaCommerce.Infrastructure;
using NexaCommerce.Models;
using NexaCommerce.Services;

namespace NexaCommerce.Controllers;

[Authorize]
public class CheckoutController : BaseController
{
    private readonly CartService _cart;
    private readonly AddressService _addresses;
    private readonly OrderService _orders;
    private readonly AuthService _auth;
    private readonly AuditService _audit;

    public CheckoutController(CartService cart, AddressService addresses, OrderService orders, AuthService auth, AuditService audit)
    {
        _cart = cart; _addresses = addresses; _orders = orders; _auth = auth; _audit = audit;
    }

    public async Task<IActionResult> Index()
    {
        var cart = await _cart.GetCartAsync();
        if (cart.Items.Count == 0) { Info("Your cart is empty."); return RedirectToAction("Index", "Cart"); }
        var addresses = await _addresses.ForUserAsync(CurrentUserId);
        var def = addresses.FirstOrDefault(a => a.IsDefault) ?? addresses.FirstOrDefault();
        var user = await _auth.GetByIdAsync(CurrentUserId);
        var vm = new CheckoutVm
        {
            Cart = cart, Addresses = addresses, AddressId = def?.AddressId,
            ShipName = def?.RecipientName ?? user?.FullName, ShipPhone = def?.Phone ?? user?.Phone,
            ShipAddress = def?.Line1, ShipCity = def?.City
        };
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> ApplyCoupon(string code)
    {
        var cart = await _cart.GetCartAsync();
        var (ok, discount, message) = await _orders.PreviewCouponAsync(code, cart.SubTotal);
        return Json(new { ok, discount, message, discountText = Fmt.Money(discount), totalText = Fmt.Money(cart.Total - (ok ? discount : 0)) });
    }

    [HttpPost]
    public async Task<IActionResult> Index(CheckoutVm vm)
    {
        vm.Cart = await _cart.GetCartAsync();
        vm.Addresses = await _addresses.ForUserAsync(CurrentUserId);
        if (vm.Cart.Items.Count == 0) return RedirectToAction("Index", "Cart");
        if (!new[] { "COD", "bKash", "Card" }.Contains(vm.PaymentMethod)) ModelState.AddModelError(nameof(vm.PaymentMethod), "Choose a payment method.");
        if (!ModelState.IsValid) return View(vm);

        decimal expected = vm.Cart.Total;
        if (!string.IsNullOrWhiteSpace(vm.CouponCode))
        {
            var (ok, discount, msg) = await _orders.PreviewCouponAsync(vm.CouponCode, vm.Cart.SubTotal);
            if (!ok) { ModelState.AddModelError(nameof(vm.CouponCode), msg); return View(vm); }
            expected -= discount;
        }

        var (orderId, message, paymentMessage) = await _orders.PlaceAsync(_cart.CartKey, CurrentUserId, vm, expected);
        if (orderId == 0)
        {
            ModelState.AddModelError(string.Empty, message);
            return View(vm);
        }

        if (vm.SaveAddress && !vm.Addresses.Any(a => a.Line1 == vm.ShipAddress && a.City == vm.ShipCity))
            await _addresses.SaveAsync(new Address { UserId = CurrentUserId, Label = "Saved", RecipientName = vm.ShipName, Phone = vm.ShipPhone, Line1 = vm.ShipAddress, City = vm.ShipCity });

        await _audit.LogAsync("Place", "Order", orderId, $"Checkout via {vm.PaymentMethod}");
        TempData["PaymentMessage"] = paymentMessage;
        return RedirectToAction(nameof(Confirmation), new { id = orderId });
    }

    public async Task<IActionResult> Confirmation(int id)
    {
        var order = await _orders.GetAsync(id);
        if (order == null || order.UserId != CurrentUserId) return NotFound();
        return View(new OrderConfirmationVm { Order = order, PaymentMessage = TempData["PaymentMessage"] as string });
    }
}

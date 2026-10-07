using Microsoft.AspNetCore.Mvc;
using NexaCommerce.Infrastructure;
using NexaCommerce.Services;
using NexaCommerce.Services.Intelligence;

namespace NexaCommerce.Controllers;

public class CartController : BaseController
{
    private readonly CartService _cart;
    private readonly BasketAnalysisService _basket;

    public CartController(CartService cart, BasketAnalysisService basket) { _cart = cart; _basket = basket; }

    public async Task<IActionResult> Index()
    {
        var vm = await _cart.GetCartAsync();
        vm.Suggestions = await _basket.ForCartAsync(vm.Items.Select(i => i.ProductId));
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Add(int productId, int quantity = 1, string returnUrl = null)
    {
        var (ok, message) = await _cart.AddAsync(productId, quantity);
        if (ok) Success(message); else Error(message);
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Update(int productId, int quantity)
    {
        var (ok, message) = await _cart.UpdateAsync(productId, quantity);
        if (!ok) Error(message);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Remove(int productId)
    {
        await _cart.RemoveAsync(productId);
        Info("Item removed from your cart.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Clear()
    {
        await _cart.ClearAsync();
        return RedirectToAction(nameof(Index));
    }
}

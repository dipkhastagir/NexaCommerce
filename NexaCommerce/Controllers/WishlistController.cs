using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaCommerce.Infrastructure;
using NexaCommerce.Services;

namespace NexaCommerce.Controllers;

[Authorize]
public class WishlistController : BaseController
{
    private readonly WishlistService _wishlist;
    public WishlistController(WishlistService wishlist) => _wishlist = wishlist;

    public async Task<IActionResult> Index() => View(await _wishlist.ItemsAsync(CurrentUserId));

    [HttpPost]
    public async Task<IActionResult> Toggle(int productId, string returnUrl = null)
    {
        var added = await _wishlist.ToggleAsync(CurrentUserId, productId);
        Info(added ? "Saved to your wishlist." : "Removed from your wishlist.");
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction(nameof(Index));
    }
}

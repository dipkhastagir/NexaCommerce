using Microsoft.AspNetCore.Mvc;
using NexaCommerce.Infrastructure;
using NexaCommerce.Services;

namespace NexaCommerce.Controllers;

/// <summary>Side-by-side product comparison. Selection is kept in a small cookie (max 4 products).</summary>
public class CompareController : BaseController
{
    private const string Cookie = "nx_compare";
    private readonly ProductService _products;
    public CompareController(ProductService products) => _products = products;

    private List<int> Ids() => (Request.Cookies[Cookie] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => int.TryParse(s, out var i) ? i : 0).Where(i => i > 0).Distinct().Take(4).ToList();

    private void Save(List<int> ids) => Response.Cookies.Append(Cookie, string.Join(",", ids),
        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(7), IsEssential = true, SameSite = SameSiteMode.Lax });

    public async Task<IActionResult> Index() => View(await _products.GetManyAsync(Ids()));

    [HttpPost]
    public IActionResult Add(int productId, string returnUrl = null)
    {
        var ids = Ids();
        if (!ids.Contains(productId))
        {
            if (ids.Count >= 4) ids.RemoveAt(0);
            ids.Add(productId);
            Save(ids);
        }
        Info("Added to comparison.");
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public IActionResult Remove(int productId)
    {
        var ids = Ids(); ids.Remove(productId); Save(ids);
        return RedirectToAction(nameof(Index));
    }
}

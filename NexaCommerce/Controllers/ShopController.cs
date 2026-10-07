using Microsoft.AspNetCore.Mvc;
using NexaCommerce.Infrastructure;
using NexaCommerce.Models;
using NexaCommerce.Services;
using NexaCommerce.Services.Intelligence;

namespace NexaCommerce.Controllers;

public class ShopController : BaseController
{
    private readonly ProductService _products;
    private readonly CategoryService _categories;
    private readonly BrandService _brands;
    private readonly BasketAnalysisService _basket;
    private readonly WishlistService _wishlist;
    private readonly OrderService _orders;
    private readonly ReviewService _reviews;

    public ShopController(ProductService products, CategoryService categories, BrandService brands, BasketAnalysisService basket,
        WishlistService wishlist, OrderService orders, ReviewService reviews)
    {
        _products = products; _categories = categories; _brands = brands; _basket = basket; _wishlist = wishlist; _orders = orders; _reviews = reviews;
    }

    public async Task<IActionResult> Index(string q, int? category, int? brand, decimal? min, decimal? max, bool instock = false, string sort = "newest", int page = 1)
    {
        var vm = new ShopVm
        {
            Search = q, CategoryId = category, BrandId = brand, MinPrice = min, MaxPrice = max, InStock = instock, Sort = sort,
            Categories = await _categories.AllAsync(true),
            Brands = await _brands.AllAsync(true),
            Products = await _products.SearchAsync(new ProductQuery
            {
                Search = q, CategoryId = category, BrandId = brand, MinPrice = min, MaxPrice = max, InStockOnly = instock, Sort = sort, Page = page, PageSize = 12
            })
        };
        if (category.HasValue)
        {
            vm.CurrentCategory = vm.Categories.FirstOrDefault(c => c.CategoryId == category.Value);
            vm.Heading = vm.CurrentCategory?.Name ?? "Products";
        }
        if (!string.IsNullOrWhiteSpace(q)) vm.Heading = $"Results for \u201C{q}\u201D";
        return View(vm);
    }

    [Route("/shop/category/{slug}")]
    public async Task<IActionResult> Category(string slug)
    {
        var c = await _categories.GetBySlugAsync(slug);
        if (c == null) return NotFound();
        return RedirectToAction(nameof(Index), new { category = c.CategoryId });
    }

    public async Task<IActionResult> Deals()
    {
        var vm = new ShopVm
        {
            Heading = "Deals",
            Categories = await _categories.AllAsync(true),
            Brands = await _brands.AllAsync(true),
            Products = new PagedResult<Product> { Items = await _products.DealsAsync(48), PageSize = 48 }
        };
        vm.Products.TotalCount = vm.Products.Items.Count;
        return View(vm);
    }

    public async Task<IActionResult> Details(int id)
    {
        var p = await _products.GetAsync(id);
        if (p == null || (!p.IsActive && !User.IsBackOffice())) return NotFound();
        var vm = new ProductDetailsVm
        {
            Product = p,
            Reviews = await _products.ApprovedReviewsAsync(id),
            Related = await _products.RelatedAsync(p),
            BoughtTogether = await _basket.ForProductAsync(id)
        };
        foreach (var r in vm.Reviews) vm.RatingHistogram[r.Rating - 1]++;
        var uid = CurrentUserId;
        if (uid > 0)
        {
            vm.InWishlist = await _wishlist.ContainsAsync(uid, id);
            vm.CanReview = await _orders.HasPurchasedAsync(uid, id) && !await _reviews.AlreadyReviewedAsync(uid, id);
        }
        return View(vm);
    }
}

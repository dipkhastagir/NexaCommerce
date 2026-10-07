using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaCommerce.Infrastructure;
using NexaCommerce.Models;
using NexaCommerce.Services;

namespace NexaCommerce.Controllers.Api;

/// <summary>JSON API over the same service layer the Razor admin uses (dual-interface architecture).</summary>
[ApiController]
[Route("api/products")]
public class ProductsApiController : ControllerBase
{
    private readonly ProductService _products;
    private readonly AuditService _audit;

    public ProductsApiController(ProductService products, AuditService audit) { _products = products; _audit = audit; }

    /// <summary>GET /api/products?q=&amp;categoryId=&amp;page=1&amp;pageSize=20&amp;sort=newest</summary>
    [HttpGet]
    public async Task<IActionResult> List(string q, int? categoryId, int? brandId, decimal? minPrice, decimal? maxPrice, string sort = "newest", int page = 1, int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var result = await _products.SearchAsync(new ProductQuery
        {
            Search = q, CategoryId = categoryId, BrandId = brandId, MinPrice = minPrice, MaxPrice = maxPrice, Sort = sort, Page = page, PageSize = pageSize
        });
        return Ok(new
        {
            page = result.Page, pageSize = result.PageSize, total = result.TotalCount, totalPages = result.TotalPages,
            items = result.Items.Select(Shape)
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var p = await _products.GetAsync(id);
        return p == null ? NotFound(new { error = "Product not found" }) : Ok(Shape(p));
    }

    [HttpPost, Authorize(Policy = "Management")]
    public async Task<IActionResult> Create([FromBody] ProductFormVm vm)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var id = await _products.CreateAsync(vm, User.GetUserId());
        if (id < 0) return Conflict(new { error = "SKU already exists" });
        await _audit.LogAsync("Create", "Product", id, "via API");
        return CreatedAtAction(nameof(Get), new { id }, Shape(await _products.GetAsync(id)));
    }

    [HttpPut("{id:int}"), Authorize(Policy = "Management")]
    public async Task<IActionResult> Update(int id, [FromBody] ProductFormVm vm)
    {
        vm.ProductId = id;
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var r = await _products.UpdateAsync(vm);
        if (r < 0) return Conflict(new { error = "SKU already exists" });
        if (r == 0) return NotFound(new { error = "Product not found" });
        await _audit.LogAsync("Update", "Product", id, "via API");
        return Ok(Shape(await _products.GetAsync(id)));
    }

    [HttpDelete("{id:int}"), Authorize(Policy = "Management")]
    public async Task<IActionResult> Delete(int id)
    {
        var r = await _products.DeleteAsync(id);
        await _audit.LogAsync(r, "Product", id, "via API");
        return Ok(new { result = r });
    }

    internal static object Shape(Product p) => new
    {
        id = p.ProductId, sku = p.Sku, name = p.Name, slug = p.Slug, description = p.Description,
        category = new { id = p.CategoryId, name = p.CategoryName },
        brand = p.BrandId == null ? null : new { id = p.BrandId, name = p.BrandName },
        price = p.Price, salePrice = p.DiscountPrice, effectivePrice = p.EffectivePrice,
        stock = p.StockQuantity, stockStatus = p.StockStatus, rating = p.AvgRating, reviews = p.ReviewCount,
        unitsSold = p.UnitsSold, isActive = p.IsActive, isFeatured = p.IsFeatured
    };
}

[ApiController]
[Route("api/categories")]
public class CategoriesApiController : ControllerBase
{
    private readonly CategoryService _categories;
    private readonly ProductService _products;
    private readonly AuditService _audit;

    public CategoriesApiController(CategoryService categories, ProductService products, AuditService audit)
    {
        _categories = categories; _products = products; _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> List() => Ok((await _categories.AllAsync(true)).Select(c => new
    {
        id = c.CategoryId, name = c.Name, slug = c.Slug, description = c.Description, parentId = c.ParentCategoryId, icon = c.Icon, products = c.ProductCount
    }));

    [HttpGet("{id:int}/products")]
    public async Task<IActionResult> Products(int id, int page = 1)
    {
        var r = await _products.SearchAsync(new ProductQuery { CategoryId = id, Page = page, PageSize = 50 });
        return Ok(new { total = r.TotalCount, items = r.Items.Select(ProductsApiController.Shape) });
    }

    [HttpPost, Authorize(Policy = "Management")]
    public async Task<IActionResult> Create([FromBody] Category c)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        c.CategoryId = 0;
        var id = await _categories.SaveAsync(c);
        if (id < 0) return Conflict(new { error = "Slug already exists" });
        await _audit.LogAsync("Create", "Category", id, "via API");
        return Ok(new { id });
    }
}

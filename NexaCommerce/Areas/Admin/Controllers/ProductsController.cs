using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using NexaCommerce.Infrastructure;
using NexaCommerce.Models;
using NexaCommerce.Services;
using NexaCommerce.Services.Intelligence;

namespace NexaCommerce.Areas.Admin.Controllers;

public class ProductsController : AdminBaseController
{
    private readonly ProductService _products;
    private readonly CategoryService _categories;
    private readonly BrandService _brands;
    private readonly SupplierService _suppliers;
    private readonly InventoryService _inventory;
    private readonly ForecastingService _forecast;
    private readonly AuditService _audit;

    public ProductsController(ProductService products, CategoryService categories, BrandService brands, SupplierService suppliers,
        InventoryService inventory, ForecastingService forecast, AuditService audit)
    {
        _products = products; _categories = categories; _brands = brands; _suppliers = suppliers; _inventory = inventory; _forecast = forecast; _audit = audit;
    }

    public async Task<IActionResult> Index(string q, int? category, string stock, string sort = "newest", int page = 1)
    {
        ViewBag.Categories = await _categories.AllAsync();
        ViewBag.Q = q; ViewBag.Category = category; ViewBag.Stock = stock; ViewBag.Sort = sort;
        return View(await _products.SearchAsync(new ProductQuery
        {
            Search = q, CategoryId = category, StockStatus = stock, Sort = sort, Page = page, PageSize = 15, ActiveOnly = false
        }));
    }

    public async Task<IActionResult> Details(int id)
    {
        var p = await _products.GetAsync(id);
        if (p == null) return NotFound();
        ViewBag.Movements = (await _inventory.MovementsAsync(id, null, null, null, 1, 10)).Items;
        ViewBag.Forecast = await _forecast.ForecastProductAsync(id);
        return View(p);
    }

    [Authorize(Policy = "Management")]
    public async Task<IActionResult> Create() => View("Form", await FillAsync(new ProductFormVm()));

    [HttpPost, Authorize(Policy = "Management")]
    public async Task<IActionResult> Create(ProductFormVm vm)
    {
        Validate(vm);
        if (!ModelState.IsValid) return View("Form", await FillAsync(vm));
        var id = await _products.CreateAsync(vm, CurrentUserId);
        if (id < 0)
        {
            ModelState.AddModelError(nameof(vm.Sku), "Another product already uses this SKU.");
            return View("Form", await FillAsync(vm));
        }
        await _audit.LogAsync("Create", "Product", id, $"{vm.Sku} {vm.Name}");
        Success($"{vm.Name} created.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = "Management")]
    public async Task<IActionResult> Edit(int id)
    {
        var p = await _products.GetAsync(id);
        if (p == null) return NotFound();
        var vm = new ProductFormVm
        {
            ProductId = p.ProductId, Sku = p.Sku, Name = p.Name, Description = p.Description, CategoryId = p.CategoryId, BrandId = p.BrandId,
            SupplierId = p.SupplierId, Price = p.Price, CostPrice = p.CostPrice, DiscountPrice = p.DiscountPrice, StockQuantity = p.StockQuantity,
            ReorderLevel = p.ReorderLevel, IsActive = p.IsActive, IsFeatured = p.IsFeatured
        };
        return View("Form", await FillAsync(vm));
    }

    [HttpPost, Authorize(Policy = "Management")]
    public async Task<IActionResult> Edit(int id, ProductFormVm vm)
    {
        vm.ProductId = id;
        Validate(vm);
        if (!ModelState.IsValid) return View("Form", await FillAsync(vm));
        var r = await _products.UpdateAsync(vm);
        if (r < 0)
        {
            ModelState.AddModelError(nameof(vm.Sku), "Another product already uses this SKU.");
            return View("Form", await FillAsync(vm));
        }
        await _audit.LogAsync("Update", "Product", id, $"{vm.Sku} price {vm.Price}");
        Success("Product saved.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Policy = "Management")]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await _products.GetAsync(id);
        return p == null ? NotFound() : View(p);
    }

    [HttpPost, ActionName("Delete"), Authorize(Policy = "Management")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var r = await _products.DeleteAsync(id);
        await _audit.LogAsync(r, "Product", id);
        Success(r == "Archived" ? "This product has sales history, so it was archived (hidden from the store) instead of deleted." : "Product deleted.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Policy = "Management")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        await _products.ToggleActiveAsync(id);
        await _audit.LogAsync("ToggleActive", "Product", id);
        return RedirectToAction(nameof(Details), new { id });
    }

    private void Validate(ProductFormVm vm)
    {
        if (vm.DiscountPrice.HasValue && vm.DiscountPrice >= vm.Price)
            ModelState.AddModelError(nameof(vm.DiscountPrice), "Sale price must be lower than the selling price.");
        if (vm.CostPrice > (vm.DiscountPrice ?? vm.Price))
            ModelState.AddModelError(nameof(vm.CostPrice), "Cost is higher than the price customers pay - this product would sell at a loss.");
    }

    private async Task<ProductFormVm> FillAsync(ProductFormVm vm)
    {
        vm.Categories = (await _categories.AllAsync()).Select(c => new SelectListItem(c.ParentName == null ? c.Name : c.ParentName + " / " + c.Name, c.CategoryId.ToString()));
        vm.Brands = (await _brands.AllAsync()).Select(b => new SelectListItem(b.Name, b.BrandId.ToString()));
        vm.Suppliers = (await _suppliers.AllAsync()).Select(s => new SelectListItem(s.Name, s.SupplierId.ToString()));
        return vm;
    }
}

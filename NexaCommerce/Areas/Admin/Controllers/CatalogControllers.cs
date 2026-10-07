using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using NexaCommerce.Infrastructure;
using NexaCommerce.Models;
using NexaCommerce.Services;

namespace NexaCommerce.Areas.Admin.Controllers;

[Authorize(Policy = "Management")]
public class CategoriesController : AdminBaseController
{
    private readonly CategoryService _categories;
    private readonly AuditService _audit;
    public CategoriesController(CategoryService categories, AuditService audit) { _categories = categories; _audit = audit; }

    public async Task<IActionResult> Index() => View(await _categories.AllAsync());

    public async Task<IActionResult> Create() { await ParentsAsync(0); return View("Form", new Category()); }

    [HttpPost]
    public async Task<IActionResult> Create(Category model) => await SaveAsync(model, true);

    public async Task<IActionResult> Edit(int id)
    {
        var c = await _categories.GetAsync(id);
        if (c == null) return NotFound();
        await ParentsAsync(id);
        return View("Form", c);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, Category model) { model.CategoryId = id; return await SaveAsync(model, false); }

    public async Task<IActionResult> Delete(int id)
    {
        var c = await _categories.GetAsync(id);
        return c == null ? NotFound() : View(c);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        if (await _categories.DeleteAsync(id)) { await _audit.LogAsync("Delete", "Category", id); Success("Category deleted."); }
        else Error("This category still has products or sub-categories. Move them first.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> SaveAsync(Category model, bool creating)
    {
        if (!ModelState.IsValid) { await ParentsAsync(model.CategoryId); return View("Form", model); }
        var id = await _categories.SaveAsync(model);
        if (id < 0)
        {
            ModelState.AddModelError(nameof(model.Slug), "Another category already uses this URL slug.");
            await ParentsAsync(model.CategoryId);
            return View("Form", model);
        }
        await _audit.LogAsync(creating ? "Create" : "Update", "Category", id, model.Name);
        Success($"Category \u201C{model.Name}\u201D saved.");
        return RedirectToAction(nameof(Index));
    }

    private async Task ParentsAsync(int exclude)
        => ViewBag.Parents = (await _categories.AllAsync()).Where(c => c.CategoryId != exclude && c.ParentCategoryId == null)
            .Select(c => new SelectListItem(c.Name, c.CategoryId.ToString())).ToList();
}

[Authorize(Policy = "Management")]
public class BrandsController : AdminBaseController
{
    private readonly BrandService _brands;
    private readonly AuditService _audit;
    public BrandsController(BrandService brands, AuditService audit) { _brands = brands; _audit = audit; }

    public async Task<IActionResult> Index() => View(await _brands.AllAsync());
    public IActionResult Create() => View("Form", new Brand());

    [HttpPost]
    public async Task<IActionResult> Create(Brand model) => await SaveAsync(model);

    public async Task<IActionResult> Edit(int id)
    {
        var b = await _brands.GetAsync(id);
        return b == null ? NotFound() : View("Form", b);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, Brand model) { model.BrandId = id; return await SaveAsync(model); }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        if (await _brands.DeleteAsync(id)) { await _audit.LogAsync("Delete", "Brand", id); Success("Brand deleted."); }
        else Error("Products still use this brand.");
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> SaveAsync(Brand model)
    {
        if (!ModelState.IsValid) return View("Form", model);
        if (!await _brands.SaveAsync(model)) { ModelState.AddModelError(nameof(model.Name), "A brand with this name already exists."); return View("Form", model); }
        await _audit.LogAsync(model.BrandId == 0 ? "Create" : "Update", "Brand", model.BrandId, model.Name);
        Success("Brand saved.");
        return RedirectToAction(nameof(Index));
    }
}

[Authorize(Policy = "Management")]
public class SuppliersController : AdminBaseController
{
    private readonly SupplierService _suppliers;
    private readonly AuditService _audit;
    public SuppliersController(SupplierService suppliers, AuditService audit) { _suppliers = suppliers; _audit = audit; }

    public async Task<IActionResult> Index() => View(await _suppliers.AllAsync());

    public async Task<IActionResult> Details(int id)
    {
        var s = await _suppliers.GetAsync(id);
        if (s == null) return NotFound();
        ViewBag.Products = await _suppliers.ProductsAsync(id);
        ViewBag.PurchaseOrders = await _suppliers.PurchaseOrdersAsync(id);
        return View(s);
    }

    public IActionResult Create() => View("Form", new Supplier());

    [HttpPost]
    public async Task<IActionResult> Create(Supplier model) => await SaveAsync(model);

    public async Task<IActionResult> Edit(int id)
    {
        var s = await _suppliers.GetAsync(id);
        return s == null ? NotFound() : View("Form", s);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, Supplier model) { model.SupplierId = id; return await SaveAsync(model); }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        if (await _suppliers.DeleteAsync(id)) { await _audit.LogAsync("Delete", "Supplier", id); Success("Supplier deleted."); return RedirectToAction(nameof(Index)); }
        Error("This supplier has products or purchase orders. Mark it inactive instead.");
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<IActionResult> SaveAsync(Supplier model)
    {
        if (!ModelState.IsValid) return View("Form", model);
        var id = await _suppliers.SaveAsync(model);
        await _audit.LogAsync(model.SupplierId == 0 ? "Create" : "Update", "Supplier", id, model.Name);
        Success("Supplier saved.");
        return RedirectToAction(nameof(Details), new { id });
    }
}

[Authorize(Policy = "Management")]
public class WarehousesController : AdminBaseController
{
    private readonly WarehouseService _warehouses;
    private readonly AuditService _audit;
    public WarehousesController(WarehouseService warehouses, AuditService audit) { _warehouses = warehouses; _audit = audit; }

    public async Task<IActionResult> Index()
    {
        ViewBag.Movements = await _warehouses.MovementCountsAsync();
        return View(await _warehouses.AllAsync());
    }

    public async Task<IActionResult> Form(int? id)
    {
        if (id == null) return View(new Warehouse());
        var w = await _warehouses.GetAsync(id.Value);
        return w == null ? NotFound() : View(w);
    }

    [HttpPost]
    public async Task<IActionResult> Form(Warehouse model)
    {
        if (!ModelState.IsValid) return View(model);
        await _warehouses.SaveAsync(model);
        await _audit.LogAsync(model.WarehouseId == 0 ? "Create" : "Update", "Warehouse", model.WarehouseId, model.Name);
        Success("Warehouse saved.");
        return RedirectToAction(nameof(Index));
    }
}

[Authorize(Policy = "Management")]
public class CouponsController : AdminBaseController
{
    private readonly CouponService _coupons;
    private readonly AuditService _audit;
    public CouponsController(CouponService coupons, AuditService audit) { _coupons = coupons; _audit = audit; }

    public async Task<IActionResult> Index()
    {
        ViewBag.Usage = await _coupons.UsageAsync();
        return View(await _coupons.AllAsync());
    }

    public async Task<IActionResult> Form(int? id)
    {
        if (id == null) return View(new Coupon());
        var c = await _coupons.GetAsync(id.Value);
        return c == null ? NotFound() : View(c);
    }

    [HttpPost]
    public async Task<IActionResult> Form(Coupon model)
    {
        if (model.EndsAt <= model.StartsAt) ModelState.AddModelError(nameof(model.EndsAt), "End date must be after the start date.");
        if (model.DiscountType == "Percent" && model.Value > 90) ModelState.AddModelError(nameof(model.Value), "Percentage discounts are capped at 90%.");
        if (!ModelState.IsValid) return View(model);
        if (!await _coupons.SaveAsync(model)) { ModelState.AddModelError(nameof(model.Code), "This code is already in use."); return View(model); }
        await _audit.LogAsync(model.CouponId == 0 ? "Create" : "Update", "Coupon", model.CouponId, model.Code);
        Success($"Coupon {model.Code} saved.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        await _coupons.DeleteAsync(id);
        await _audit.LogAsync("Delete", "Coupon", id);
        Success("Coupon deleted.");
        return RedirectToAction(nameof(Index));
    }
}

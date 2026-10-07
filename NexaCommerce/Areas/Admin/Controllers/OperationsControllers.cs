using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using NexaCommerce.Infrastructure;
using NexaCommerce.Models;
using NexaCommerce.Services;
using NexaCommerce.Services.Intelligence;

namespace NexaCommerce.Areas.Admin.Controllers;

public class InventoryController : AdminBaseController
{
    private readonly ProductService _products;
    private readonly InventoryService _inventory;
    private readonly WarehouseService _warehouses;
    private readonly CategoryService _categories;
    private readonly AuditService _audit;

    public InventoryController(ProductService products, InventoryService inventory, WarehouseService warehouses, CategoryService categories, AuditService audit)
    {
        _products = products; _inventory = inventory; _warehouses = warehouses; _categories = categories; _audit = audit;
    }

    public async Task<IActionResult> Index(string q, int? category, string stock, int page = 1)
    {
        ViewBag.Categories = await _categories.AllAsync();
        ViewBag.Q = q; ViewBag.Category = category; ViewBag.Stock = stock;
        return View(await _products.SearchAsync(new ProductQuery { Search = q, CategoryId = category, StockStatus = stock, Sort = "stock", Page = page, PageSize = 20, ActiveOnly = false }));
    }

    public async Task<IActionResult> LowStock() => View(await _products.LowStockAsync());

    public async Task<IActionResult> Adjust(int id)
    {
        var p = await _products.GetAsync(id);
        if (p == null) return NotFound();
        return View(await FillAsync(new StockAdjustVm { ProductId = id, ProductName = p.Name, Sku = p.Sku, CurrentStock = p.StockQuantity }));
    }

    [HttpPost]
    public async Task<IActionResult> Adjust(StockAdjustVm vm)
    {
        if (vm.Quantity == 0) ModelState.AddModelError(nameof(vm.Quantity), "Enter a non-zero quantity.");
        if (!new[] { "ADJUSTMENT", "PURCHASE", "RETURN", "DAMAGE", "COUNT" }.Contains(vm.MovementType)) ModelState.AddModelError(nameof(vm.MovementType), "Choose a type.");
        if (vm.MovementType == "DAMAGE" && vm.Quantity > 0) vm.Quantity = -vm.Quantity;
        if (!ModelState.IsValid) return View(await FillAsync(vm));
        var (ok, message, balance) = await _inventory.AdjustAsync(vm.ProductId, vm.Quantity, vm.MovementType, vm.Reference, vm.Note, vm.WarehouseId, CurrentUserId);
        if (!ok) { ModelState.AddModelError(string.Empty, message); return View(await FillAsync(vm)); }
        await _audit.LogAsync("StockAdjust", "Product", vm.ProductId, $"{vm.MovementType} {vm.Quantity:+#;-#} -> {balance}: {vm.Note}");
        Success($"Stock updated. New balance: {balance} units.");
        return RedirectToAction(nameof(Movements), new { productId = vm.ProductId });
    }

    public async Task<IActionResult> Movements(int? productId, string type, DateTime? from, DateTime? to, int page = 1)
    {
        ViewBag.ProductId = productId; ViewBag.Type = type; ViewBag.From = from; ViewBag.To = to;
        ViewBag.Product = productId.HasValue ? await _products.GetAsync(productId.Value) : null;
        return View(await _inventory.MovementsAsync(productId, type, from, to, page));
    }

    private async Task<StockAdjustVm> FillAsync(StockAdjustVm vm)
    {
        var p = await _products.GetAsync(vm.ProductId);
        if (p != null) { vm.ProductName = p.Name; vm.Sku = p.Sku; vm.CurrentStock = p.StockQuantity; }
        vm.Warehouses = (await _warehouses.AllAsync(true)).Select(w => new SelectListItem(w.Name, w.WarehouseId.ToString()));
        return vm;
    }
}

[Authorize(Policy = "Management")]
public class PurchaseOrdersController : AdminBaseController
{
    private readonly PurchaseOrderService _pos;
    private readonly SupplierService _suppliers;
    private readonly WarehouseService _warehouses;
    private readonly ProductService _products;
    private readonly ReorderService _reorder;
    private readonly AuditService _audit;

    public PurchaseOrdersController(PurchaseOrderService pos, SupplierService suppliers, WarehouseService warehouses, ProductService products,
        ReorderService reorder, AuditService audit)
    {
        _pos = pos; _suppliers = suppliers; _warehouses = warehouses; _products = products; _reorder = reorder; _audit = audit;
    }

    public async Task<IActionResult> Index(string status)
    {
        ViewBag.Status = status;
        return View(await _pos.AllAsync(status));
    }

    public async Task<IActionResult> Details(int id)
    {
        var po = await _pos.GetAsync(id);
        return po == null ? NotFound() : View(po);
    }

    /// <param name="fromReorder">Pre-fills lines from the reorder recommendations for this supplier.</param>
    public async Task<IActionResult> Create(int? supplierId, bool fromReorder = false)
    {
        var vm = new PurchaseOrderFormVm { SupplierId = supplierId ?? 0 };
        if (fromReorder && supplierId.HasValue)
        {
            var suggestions = (await _reorder.SuggestAsync()).Where(s => s.SupplierId == supplierId && s.SuggestedQty > 0);
            vm.Lines = suggestions.Select(s => new PurchaseOrderLineVm { ProductId = s.ProductId, Quantity = s.SuggestedQty, UnitCost = s.CostPrice }).ToList();
            vm.Notes = "Generated from reorder recommendations (EOQ / reorder point).";
        }
        return View(await FillAsync(vm));
    }

    [HttpPost]
    public async Task<IActionResult> Create(PurchaseOrderFormVm vm, string submit)
    {
        var lines = vm.Lines?.Where(l => l.ProductId > 0 && l.Quantity > 0).ToList() ?? new();
        if (lines.Count == 0) ModelState.AddModelError(string.Empty, "Add at least one product line with a quantity.");
        if (lines.Any(l => l.UnitCost <= 0)) ModelState.AddModelError(string.Empty, "Every line needs a unit cost above zero.");
        if (!ModelState.IsValid) return View(await FillAsync(vm));
        var id = await _pos.CreateAsync(vm, CurrentUserId, submit == "order");
        await _audit.LogAsync("Create", "PurchaseOrder", id, submit == "order" ? "Placed" : "Draft");
        Success(submit == "order" ? "Purchase order placed with the supplier." : "Purchase order saved as draft.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Place(int id) => await StatusAsync(id, "Ordered", "Purchase order placed.");

    [HttpPost]
    public async Task<IActionResult> Cancel(int id) => await StatusAsync(id, "Cancelled", "Purchase order cancelled.");

    [HttpPost]
    public async Task<IActionResult> Receive(int id)
    {
        var r = await _pos.ReceiveAsync(id, CurrentUserId);
        if (r == "OK")
        {
            await _audit.LogAsync("Receive", "PurchaseOrder", id);
            Success("Goods received. Stock levels and cost prices are updated.");
        }
        else Error(r);
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<IActionResult> StatusAsync(int id, string status, string message)
    {
        var r = await _pos.SetStatusAsync(id, status);
        if (r == "OK") { await _audit.LogAsync(status, "PurchaseOrder", id); Success(message); } else Error(r);
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<PurchaseOrderFormVm> FillAsync(PurchaseOrderFormVm vm)
    {
        vm.Suppliers = (await _suppliers.AllAsync(true)).Select(s => new SelectListItem(s.Name, s.SupplierId.ToString()));
        vm.Warehouses = (await _warehouses.AllAsync(true)).Select(w => new SelectListItem(w.Name, w.WarehouseId.ToString()));
        vm.Products = await _products.AllAsync(true);
        while (vm.Lines.Count < 5) vm.Lines.Add(new PurchaseOrderLineVm());
        return vm;
    }
}

public class OrdersController : AdminBaseController
{
    private readonly OrderService _orders;
    private readonly ReturnService _returns;
    private readonly RiskScoringService _risk;
    private readonly AuditService _audit;

    public OrdersController(OrderService orders, ReturnService returns, RiskScoringService risk, AuditService audit)
    {
        _orders = orders; _returns = returns; _risk = risk; _audit = audit;
    }

    public async Task<IActionResult> Index(string q, string status, string risk, string payment, DateTime? from, DateTime? to, int page = 1)
    {
        ViewBag.Q = q; ViewBag.Status = status; ViewBag.Risk = risk; ViewBag.Payment = payment; ViewBag.From = from; ViewBag.To = to;
        return View(await _orders.SearchAsync(new OrderQuery { Search = q, Status = status, Risk = risk, Payment = payment, From = from, To = to, Page = page }));
    }

    public async Task<IActionResult> Details(int id)
    {
        var o = await _orders.GetAsync(id);
        if (o == null) return NotFound();
        ViewBag.Returns = await _returns.ForOrderAsync(id);
        return View(o);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStatus(int id, string status, string note)
    {
        var r = await _orders.UpdateStatusAsync(id, status, note, CurrentUserId);
        if (r == "OK") { await _audit.LogAsync("Status:" + status, "Order", id, note); Success($"Order marked {status.ToLower()}."); }
        else Error(r);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Rescore(int id)
    {
        var (score, level, _) = await _risk.ScoreOrderAsync(id);
        Info($"Risk re-scored: {score}/100 ({level}).");
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Invoice(int id)
    {
        var o = await _orders.GetAsync(id);
        return o == null ? NotFound() : View(o);
    }

    public async Task<IActionResult> Export(string status, DateTime? from, DateTime? to)
    {
        var r = await _orders.SearchAsync(new OrderQuery { Status = status, From = from, To = to, Page = 1, PageSize = 100000 });
        var sb = new System.Text.StringBuilder("Order,Date,Customer,Email,Status,Payment,PaymentStatus,Items,Subtotal,Discount,Shipping,Total,City,Risk\n");
        foreach (var o in r.Items)
            sb.AppendLine(string.Join(",", new object[] { o.OrderNumber, o.CreatedAt.ToString("yyyy-MM-dd HH:mm"), o.CustomerName, o.CustomerEmail, o.Status, o.PaymentMethod,
                o.PaymentStatus, o.ItemCount, o.SubTotal, o.DiscountAmount, o.ShippingFee, o.TotalAmount, o.ShipCity, o.RiskLevel }.Select(CsvWriter.Escape)));
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"orders-{DateTime.Now:yyyyMMdd}.csv");
    }
}

public class CustomersController : AdminBaseController
{
    private readonly CustomerService _customers;
    private readonly OrderService _orders;
    private readonly AddressService _addresses;
    private readonly SegmentationService _rfm;
    private readonly UserService _users;
    private readonly AuditService _audit;

    public CustomersController(CustomerService customers, OrderService orders, AddressService addresses, SegmentationService rfm, UserService users, AuditService audit)
    {
        _customers = customers; _orders = orders; _addresses = addresses; _rfm = rfm; _users = users; _audit = audit;
    }

    public async Task<IActionResult> Index(string q, string sort = "spent", int page = 1)
    {
        ViewBag.Q = q; ViewBag.Sort = sort;
        return View(await _customers.SearchAsync(q, sort, page));
    }

    public async Task<IActionResult> Details(int id)
    {
        var c = await _customers.GetAsync(id);
        if (c == null) return NotFound();
        var vm = new CustomerDetailsVm
        {
            Customer = c,
            Orders = (await _orders.SearchAsync(new OrderQuery { UserId = id, PageSize = 50 })).Items,
            Addresses = await _addresses.ForUserAsync(id),
            Rfm = (await _rfm.ScoreAsync()).FirstOrDefault(r => r.UserId == id)
        };
        return View(vm);
    }

    [HttpPost, Authorize(Policy = "Management")]
    public async Task<IActionResult> ToggleActive(int id, bool active)
    {
        await _users.SetActiveAsync(id, active);
        await _audit.LogAsync(active ? "Activate" : "Deactivate", "Customer", id);
        Success(active ? "Customer re-activated." : "Customer blocked from signing in.");
        return RedirectToAction(nameof(Details), new { id });
    }
}

public class ReviewsController : AdminBaseController
{
    private readonly ReviewService _reviews;
    private readonly AuditService _audit;
    public ReviewsController(ReviewService reviews, AuditService audit) { _reviews = reviews; _audit = audit; }

    public async Task<IActionResult> Index(string status = "pending", int page = 1)
    {
        ViewBag.Status = status;
        return View(await _reviews.SearchAsync(status == "all" ? null : status, page));
    }

    [HttpPost]
    public async Task<IActionResult> Approve(int id, bool approved, string status)
    {
        await _reviews.SetApprovedAsync(id, approved);
        await _audit.LogAsync(approved ? "Approve" : "Unpublish", "Review", id);
        return RedirectToAction(nameof(Index), new { status });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, string status)
    {
        await _reviews.DeleteAsync(id);
        await _audit.LogAsync("Delete", "Review", id);
        Success("Review deleted.");
        return RedirectToAction(nameof(Index), new { status });
    }
}

public class ReturnsController : AdminBaseController
{
    private readonly ReturnService _returns;
    private readonly AuditService _audit;
    public ReturnsController(ReturnService returns, AuditService audit) { _returns = returns; _audit = audit; }

    public async Task<IActionResult> Index(string status)
    {
        ViewBag.Status = status;
        return View(await _returns.AllAsync(status));
    }

    public async Task<IActionResult> Details(int id)
    {
        var r = await _returns.GetAsync(id);
        return r == null ? NotFound() : View(r);
    }

    [HttpPost, Authorize(Policy = "Management")]
    public async Task<IActionResult> Resolve(int id, string status, string note, decimal refund)
    {
        if (!new[] { "Approved", "Rejected", "Refunded" }.Contains(status)) { Error("Choose an outcome."); return RedirectToAction(nameof(Details), new { id }); }
        var r = await _returns.GetAsync(id);
        if (r == null) return NotFound();
        if (refund < 0 || refund > r.OrderTotal) { Error("Refund must be between zero and the order total."); return RedirectToAction(nameof(Details), new { id }); }
        await _returns.ResolveAsync(id, status, note, status == "Refunded" ? refund : 0);
        await _audit.LogAsync("Return:" + status, "Return", id, note);
        Success($"Return {status.ToLower()}.");
        return RedirectToAction(nameof(Details), new { id });
    }
}

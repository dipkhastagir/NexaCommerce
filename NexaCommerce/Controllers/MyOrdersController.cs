using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaCommerce.Infrastructure;
using NexaCommerce.Models;
using NexaCommerce.Services;

namespace NexaCommerce.Controllers;

[Authorize]
public class MyOrdersController : BaseController
{
    private readonly OrderService _orders;
    private readonly ReturnService _returns;
    private readonly ReviewService _reviews;
    private readonly ProductService _products;
    private readonly AuditService _audit;

    public MyOrdersController(OrderService orders, ReturnService returns, ReviewService reviews, ProductService products, AuditService audit)
    {
        _orders = orders; _returns = returns; _reviews = reviews; _products = products; _audit = audit;
    }

    public async Task<IActionResult> Index(string status, int page = 1)
        => View(await _orders.SearchAsync(new OrderQuery { UserId = CurrentUserId, Status = status, Page = page, PageSize = 10 }));

    public async Task<IActionResult> Details(int id)
    {
        var order = await _orders.GetAsync(id);
        if (order == null || order.UserId != CurrentUserId) return NotFound();
        ViewBag.Returns = await _returns.ForOrderAsync(id);
        return View(order);
    }

    public async Task<IActionResult> Track(int id)
    {
        var order = await _orders.GetAsync(id);
        if (order == null || order.UserId != CurrentUserId) return NotFound();
        return View(order);
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(int id)
    {
        var order = await _orders.GetAsync(id);
        if (order == null || order.UserId != CurrentUserId) return NotFound();
        if (order.Status is not ("Pending" or "Confirmed"))
        {
            Error("This order is already being prepared and can no longer be cancelled online.");
            return RedirectToAction(nameof(Details), new { id });
        }
        var result = await _orders.UpdateStatusAsync(id, "Cancelled", "Cancelled by customer", CurrentUserId);
        if (result == "OK") { Success("Order cancelled. Any online payment will be refunded."); await _audit.LogAsync("Cancel", "Order", id, "Customer cancelled"); }
        else Error(result);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Return(int id)
    {
        var order = await _orders.GetAsync(id);
        if (order == null || order.UserId != CurrentUserId) return NotFound();
        return View(new ReturnRequestVm { OrderId = id, OrderNumber = order.OrderNumber });
    }

    [HttpPost]
    public async Task<IActionResult> Return(ReturnRequestVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var result = await _returns.RequestAsync(vm.OrderId, CurrentUserId, vm.Reason);
        if (result != "OK") { ModelState.AddModelError(string.Empty, result); return View(vm); }
        Success("Return requested. We'll review it within two working days.");
        return RedirectToAction(nameof(Details), new { id = vm.OrderId });
    }

    [HttpGet]
    public async Task<IActionResult> Review(int productId)
    {
        var p = await _products.GetAsync(productId);
        if (p == null) return NotFound();
        if (!await _orders.HasPurchasedAsync(CurrentUserId, productId))
        {
            Error("You can review products from your delivered orders.");
            return RedirectToAction("Details", "Shop", new { id = productId });
        }
        if (await _reviews.AlreadyReviewedAsync(CurrentUserId, productId))
        {
            Info("You've already reviewed this product.");
            return RedirectToAction("Details", "Shop", new { id = productId });
        }
        return View(new WriteReviewVm { ProductId = productId, ProductName = p.Name });
    }

    [HttpPost]
    public async Task<IActionResult> Review(WriteReviewVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        if (!await _orders.HasPurchasedAsync(CurrentUserId, vm.ProductId) || await _reviews.AlreadyReviewedAsync(CurrentUserId, vm.ProductId))
            return RedirectToAction("Details", "Shop", new { id = vm.ProductId });
        await _reviews.AddAsync(CurrentUserId, vm);
        Success("Thanks for your review. It will appear once approved.");
        return RedirectToAction("Details", "Shop", new { id = vm.ProductId });
    }

    public async Task<IActionResult> Invoice(int id)
    {
        var order = await _orders.GetAsync(id);
        if (order == null || order.UserId != CurrentUserId) return NotFound();
        return View("~/Areas/Admin/Views/Orders/Invoice.cshtml", order);
    }
}

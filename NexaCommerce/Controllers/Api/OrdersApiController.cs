using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaCommerce.Infrastructure;
using NexaCommerce.Services;

namespace NexaCommerce.Controllers.Api;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersApiController : ControllerBase
{
    private readonly OrderService _orders;
    private readonly AuditService _audit;

    public OrdersApiController(OrderService orders, AuditService audit) { _orders = orders; _audit = audit; }

    /// <summary>Customers see their own orders; back-office roles see all.</summary>
    [HttpGet]
    public async Task<IActionResult> List(string status, int page = 1, int pageSize = 20)
    {
        var q = new OrderQuery { Status = status, Page = page, PageSize = Math.Clamp(pageSize, 1, 100) };
        if (!User.IsBackOffice()) q.UserId = User.GetUserId();
        var r = await _orders.SearchAsync(q);
        return Ok(new
        {
            page = r.Page, total = r.TotalCount, totalPages = r.TotalPages,
            items = r.Items.Select(o => new
            {
                id = o.OrderId, number = o.OrderNumber, customer = o.CustomerName, status = o.Status, payment = o.PaymentMethod,
                paymentStatus = o.PaymentStatus, total = o.TotalAmount, items = o.ItemCount, risk = new { score = o.RiskScore, level = o.RiskLevel }, createdAt = o.CreatedAt
            })
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var o = await _orders.GetAsync(id);
        if (o == null || (!User.IsBackOffice() && o.UserId != User.GetUserId())) return NotFound(new { error = "Order not found" });
        return Ok(new
        {
            id = o.OrderId, number = o.OrderNumber, status = o.Status, payment = o.PaymentMethod, paymentStatus = o.PaymentStatus,
            subTotal = o.SubTotal, discount = o.DiscountAmount, shipping = o.ShippingFee, total = o.TotalAmount, coupon = o.CouponCode,
            shipTo = new { name = o.ShipName, phone = o.ShipPhone, address = o.ShipAddress, city = o.ShipCity },
            risk = new { score = o.RiskScore, level = o.RiskLevel, reasons = o.RiskReasons },
            items = o.Items.Select(i => new { productId = i.ProductId, sku = i.Sku, name = i.ProductName, unitPrice = i.UnitPrice, quantity = i.Quantity, lineTotal = i.LineTotal }),
            history = o.History.Select(h => new { status = h.Status, note = h.Note, at = h.ChangedAt })
        });
    }

    public class StatusChange { public string Status { get; set; } public string Note { get; set; } }

    [HttpPut("{id:int}/status"), Authorize(Policy = "BackOffice")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] StatusChange body)
    {
        var r = await _orders.UpdateStatusAsync(id, body?.Status, body?.Note, User.GetUserId());
        if (r != "OK") return BadRequest(new { error = r });
        await _audit.LogAsync("Status:" + body.Status, "Order", id, "via API");
        return Ok(new { id, status = body.Status });
    }
}

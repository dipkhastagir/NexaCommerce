using System.Data;
using Dapper;
using NexaCommerce.Data;
using NexaCommerce.Models;
using NexaCommerce.Services.Intelligence;

namespace NexaCommerce.Services;

public class OrderQuery
{
    public string Search { get; set; }
    public string Status { get; set; }
    public string Risk { get; set; }
    public string Payment { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? UserId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class OrderService
{
    public static readonly string[] Statuses = { "Pending", "Confirmed", "Processing", "Shipped", "Delivered", "Cancelled" };

    private readonly DbConnectionFactory _db;
    private readonly SettingsService _settings;
    private readonly IPaymentGateway _gateway;
    private readonly RiskScoringService _risk;
    private readonly NotificationService _notify;
    private readonly InventoryService _inventory;

    public OrderService(DbConnectionFactory db, SettingsService settings, IPaymentGateway gateway, RiskScoringService risk,
        NotificationService notify, InventoryService inventory)
    {
        _db = db; _settings = settings; _gateway = gateway; _risk = risk; _notify = notify; _inventory = inventory;
    }

    public async Task<(int OrderId, string Message, string PaymentMessage)> PlaceAsync(string cartKey, int userId, CheckoutVm vm, decimal expectedTotal)
    {
        string paymentRef = null;
        bool paid = false;
        string paymentMessage;

        if (vm.PaymentMethod != "COD")
        {
            var result = await _gateway.ChargeAsync(vm.PaymentMethod, expectedTotal, vm.PaymentAccount);
            if (!result.Success) return (0, result.Message, result.Message);
            paymentRef = result.TransactionRef; paid = true; paymentMessage = result.Message;
        }
        else paymentMessage = "Pay in cash when your order arrives.";

        var p = new DynamicParameters();
        p.Add("@CartKey", cartKey);
        p.Add("@UserId", userId);
        p.Add("@ShipName", vm.ShipName);
        p.Add("@ShipPhone", vm.ShipPhone);
        p.Add("@ShipAddress", vm.ShipAddress);
        p.Add("@ShipCity", vm.ShipCity);
        p.Add("@PaymentMethod", vm.PaymentMethod);
        p.Add("@PaymentRef", paymentRef);
        p.Add("@PaymentSucceeded", paid);
        p.Add("@CouponCode", string.IsNullOrWhiteSpace(vm.CouponCode) ? null : vm.CouponCode.Trim().ToUpperInvariant());
        p.Add("@Notes", vm.Notes);
        p.Add("@ShippingFee", await _settings.GetDecimalAsync("ShippingFee", 60));
        p.Add("@FreeShippingThreshold", await _settings.GetDecimalAsync("FreeShippingThreshold", 2000));
        p.Add("@OrderId", dbType: DbType.Int32, direction: ParameterDirection.Output);
        p.Add("@Message", dbType: DbType.String, size: 400, direction: ParameterDirection.Output);

        using (var c = _db.Create())
            await c.ExecuteAsync("dbo.sp_PlaceOrder", p, commandType: CommandType.StoredProcedure);

        var orderId = p.Get<int?>("@OrderId") ?? 0;
        var message = p.Get<string>("@Message");
        if (orderId <= 0) return (0, message, paymentMessage);

        var scored = await _risk.ScoreOrderAsync(orderId);
        if (scored.Level == "High")
            await _notify.CreateAsync("High-risk order flagged", $"Order #{orderId} scored {scored.Score}/100: {scored.Reasons}", $"/Admin/Orders/Details/{orderId}", "Risk");

        var order = await GetAsync(orderId);
        foreach (var item in order.Items) await _inventory.CheckLowStockAsync(item.ProductId);
        await _notify.CreateAsync("New order " + order.OrderNumber, $"{order.CustomerName} placed an order for {Infrastructure.Fmt.Money(order.TotalAmount)}.",
            $"/Admin/Orders/Details/{orderId}", "Order");

        return (orderId, "OK", paymentMessage);
    }

    public async Task<(bool Ok, decimal Discount, string Message)> PreviewCouponAsync(string code, decimal subTotal)
    {
        if (string.IsNullOrWhiteSpace(code)) return (false, 0, "Enter a coupon code.");
        using var c = _db.Create();
        var coupon = await c.QueryFirstOrDefaultAsync<Coupon>("SELECT * FROM dbo.Coupons WHERE Code = @code", new { code = code.Trim().ToUpperInvariant() });
        if (coupon == null || !coupon.IsCurrentlyValid) return (false, 0, "Coupon code is invalid or expired.");
        if (subTotal < coupon.MinOrderAmount) return (false, 0, $"This coupon needs a minimum order of {Infrastructure.Fmt.Money(coupon.MinOrderAmount)}.");
        var discount = coupon.DiscountType == "Percent" ? Math.Round(subTotal * coupon.Value / 100m, 2) : coupon.Value;
        return (true, Math.Min(discount, subTotal), coupon.Description ?? "Coupon applied.");
    }

    public async Task<PagedResult<Order>> SearchAsync(OrderQuery q)
    {
        if (q.Page < 1) q.Page = 1;
        using var c = _db.Create();
        var items = (await c.QueryAsync<Order>(@"
            SELECT *, COUNT(*) OVER() AS TotalCount FROM dbo.vw_OrderSummary
            WHERE (@Search IS NULL OR OrderNumber LIKE '%' + @Search + '%' OR CustomerName LIKE '%' + @Search + '%' OR CustomerEmail LIKE '%' + @Search + '%' OR ShipPhone LIKE '%' + @Search + '%')
              AND (@Status IS NULL OR Status = @Status)
              AND (@Risk IS NULL OR RiskLevel = @Risk)
              AND (@Payment IS NULL OR PaymentMethod = @Payment)
              AND (@From IS NULL OR CreatedAt >= @From)
              AND (@To IS NULL OR CreatedAt < DATEADD(DAY, 1, @To))
              AND (@UserId IS NULL OR UserId = @UserId)
            ORDER BY OrderId DESC OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY",
            new
            {
                Search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim(),
                Status = string.IsNullOrWhiteSpace(q.Status) ? null : q.Status,
                Risk = string.IsNullOrWhiteSpace(q.Risk) ? null : q.Risk,
                Payment = string.IsNullOrWhiteSpace(q.Payment) ? null : q.Payment,
                q.From, q.To, q.UserId, q.Page, q.PageSize
            })).ToList();
        return new PagedResult<Order> { Items = items, Page = q.Page, PageSize = q.PageSize, TotalCount = items.FirstOrDefault()?.TotalCount ?? 0 };
    }

    public async Task<Order> GetAsync(int id)
    {
        using var c = _db.Create();
        var o = await c.QueryFirstOrDefaultAsync<Order>("SELECT * FROM dbo.vw_OrderSummary WHERE OrderId = @id", new { id });
        if (o == null) return null;
        o.Items = (await c.QueryAsync<OrderItem>(@"SELECT oi.*, p.Sku, cat.Icon AS CategoryIcon FROM dbo.OrderItems oi
            JOIN dbo.Products p ON p.ProductId = oi.ProductId JOIN dbo.Categories cat ON cat.CategoryId = p.CategoryId
            WHERE oi.OrderId = @id", new { id })).ToList();
        o.History = (await c.QueryAsync<OrderStatusHistory>(@"SELECT h.*, u.FullName AS ChangedByName FROM dbo.OrderStatusHistory h
            LEFT JOIN dbo.Users u ON u.UserId = h.ChangedBy WHERE h.OrderId = @id ORDER BY h.ChangedAt, h.Id", new { id })).ToList();
        o.Payments = (await c.QueryAsync<Payment>("SELECT * FROM dbo.Payments WHERE OrderId = @id ORDER BY PaymentId", new { id })).ToList();
        return o;
    }

    public async Task<string> UpdateStatusAsync(int id, string status, string note, int userId)
    {
        if (!Statuses.Contains(status)) return "Unknown status";
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<string>("dbo.sp_UpdateOrderStatus", new { OrderId = id, Status = status, Note = note, UserId = userId },
            commandType: CommandType.StoredProcedure);
    }

    public async Task<List<Order>> RecentAsync(int take = 8)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Order>("SELECT TOP (@take) * FROM dbo.vw_OrderSummary ORDER BY OrderId DESC", new { take })).ToList();
    }

    public async Task<bool> HasPurchasedAsync(int userId, int productId)
    {
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>(@"SELECT COUNT(*) FROM dbo.OrderItems oi JOIN dbo.Orders o ON o.OrderId = oi.OrderId
            WHERE o.UserId = @userId AND oi.ProductId = @productId AND o.Status = 'Delivered'", new { userId, productId }) > 0;
    }

    public static int StepIndex(string status) => Array.IndexOf(new[] { "Pending", "Confirmed", "Processing", "Shipped", "Delivered" }, status);
}

public class CouponService
{
    private readonly DbConnectionFactory _db;
    public CouponService(DbConnectionFactory db) => _db = db;

    public async Task<List<Coupon>> AllAsync()
    {
        using var c = _db.Create();
        return (await c.QueryAsync<Coupon>("SELECT * FROM dbo.Coupons ORDER BY IsActive DESC, EndsAt DESC")).ToList();
    }

    public async Task<Coupon> GetAsync(int id)
    {
        using var c = _db.Create();
        return await c.QueryFirstOrDefaultAsync<Coupon>("SELECT * FROM dbo.Coupons WHERE CouponId = @id", new { id });
    }

    public async Task<bool> SaveAsync(Coupon m)
    {
        m.Code = m.Code.Trim().ToUpperInvariant();
        using var c = _db.Create();
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Coupons WHERE Code = @Code AND CouponId <> @CouponId", new { m.Code, m.CouponId }) > 0) return false;
        if (m.CouponId == 0)
            await c.ExecuteAsync(@"INSERT INTO dbo.Coupons (Code, Description, DiscountType, Value, MinOrderAmount, MaxUses, StartsAt, EndsAt, IsActive)
                VALUES (@Code, @Description, @DiscountType, @Value, @MinOrderAmount, @MaxUses, @StartsAt, @EndsAt, @IsActive)", m);
        else
            await c.ExecuteAsync(@"UPDATE dbo.Coupons SET Code = @Code, Description = @Description, DiscountType = @DiscountType, Value = @Value,
                MinOrderAmount = @MinOrderAmount, MaxUses = @MaxUses, StartsAt = @StartsAt, EndsAt = @EndsAt, IsActive = @IsActive WHERE CouponId = @CouponId", m);
        return true;
    }

    public async Task DeleteAsync(int id)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("DELETE FROM dbo.Coupons WHERE CouponId = @id", new { id });
    }

    public async Task<Dictionary<string, (int Orders, decimal Discount)>> UsageAsync()
    {
        using var c = _db.Create();
        return (await c.QueryAsync<(string Code, int Orders, decimal Discount)>(
            "SELECT CouponCode, COUNT(*), SUM(DiscountAmount) FROM dbo.Orders WHERE CouponCode IS NOT NULL AND Status <> 'Cancelled' GROUP BY CouponCode"))
            .ToDictionary(x => x.Code, x => (x.Orders, x.Discount), StringComparer.OrdinalIgnoreCase);
    }
}

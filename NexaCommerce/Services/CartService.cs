using Dapper;
using NexaCommerce.Data;
using NexaCommerce.Models;

namespace NexaCommerce.Services;

/// <summary>Database-backed cart keyed by a browser cookie, so carts survive restarts and can be analysed.</summary>
public class CartService
{
    public const string CookieName = "nx_cart";
    private readonly DbConnectionFactory _db;
    private readonly IHttpContextAccessor _http;
    private readonly SettingsService _settings;

    public CartService(DbConnectionFactory db, IHttpContextAccessor http, SettingsService settings)
    {
        _db = db; _http = http; _settings = settings;
    }

    public string CartKey
    {
        get
        {
            var ctx = _http.HttpContext;
            if (ctx.Request.Cookies.TryGetValue(CookieName, out var key) && !string.IsNullOrWhiteSpace(key)) return key;
            if (ctx.Items.TryGetValue(CookieName, out var pending)) return (string)pending;
            key = Guid.NewGuid().ToString("N");
            ctx.Response.Cookies.Append(CookieName, key, new CookieOptions
            {
                HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Lax, Expires = DateTimeOffset.UtcNow.AddDays(30)
            });
            ctx.Items[CookieName] = key;
            return key;
        }
    }

    public async Task<List<CartItem>> ItemsAsync()
    {
        using var c = _db.Create();
        return (await c.QueryAsync<CartItem>(@"
            SELECT ci.CartItemId, ci.ProductId, ci.Quantity, p.Name, p.Sku, p.Price, p.DiscountPrice, p.StockQuantity,
                   cat.Name AS CategoryName, cat.Icon AS CategoryIcon
            FROM dbo.CartItems ci JOIN dbo.Products p ON p.ProductId = ci.ProductId
            JOIN dbo.Categories cat ON cat.CategoryId = p.CategoryId
            WHERE ci.CartKey = @key ORDER BY ci.AddedAt", new { key = CartKey })).ToList();
    }

    public async Task<CartVm> GetCartAsync()
    {
        return new CartVm
        {
            Items = await ItemsAsync(),
            ShippingFee = await _settings.GetDecimalAsync("ShippingFee", 60),
            FreeShippingThreshold = await _settings.GetDecimalAsync("FreeShippingThreshold", 2000)
        };
    }

    public async Task<int> CountAsync()
    {
        if (!_http.HttpContext.Request.Cookies.ContainsKey(CookieName)) return 0;
        using var c = _db.Create();
        return await c.ExecuteScalarAsync<int>("SELECT ISNULL(SUM(Quantity), 0) FROM dbo.CartItems WHERE CartKey = @key", new { key = CartKey });
    }

    public async Task<(bool Ok, string Message)> AddAsync(int productId, int qty)
    {
        if (qty < 1) qty = 1;
        using var c = _db.Create();
        var p = await c.QueryFirstOrDefaultAsync<Product>("SELECT ProductId, Name, StockQuantity, IsActive FROM dbo.Products WHERE ProductId = @productId", new { productId });
        if (p == null || !p.IsActive) return (false, "This product is no longer available.");
        var key = CartKey;
        var existing = await c.ExecuteScalarAsync<int?>("SELECT Quantity FROM dbo.CartItems WHERE CartKey = @key AND ProductId = @productId", new { key, productId }) ?? 0;
        var target = existing + qty;
        if (target > p.StockQuantity) return (false, p.StockQuantity == 0 ? $"{p.Name} is out of stock." : $"Only {p.StockQuantity} of {p.Name} available.");
        if (existing > 0)
            await c.ExecuteAsync("UPDATE dbo.CartItems SET Quantity = @target WHERE CartKey = @key AND ProductId = @productId", new { target, key, productId });
        else
            await c.ExecuteAsync("INSERT INTO dbo.CartItems (CartKey, ProductId, Quantity) VALUES (@key, @productId, @qty)", new { key, productId, qty });
        return (true, $"{p.Name} added to your cart.");
    }

    public async Task<(bool Ok, string Message)> UpdateAsync(int productId, int qty)
    {
        if (qty <= 0) { await RemoveAsync(productId); return (true, "Item removed."); }
        using var c = _db.Create();
        var stock = await c.ExecuteScalarAsync<int>("SELECT StockQuantity FROM dbo.Products WHERE ProductId = @productId", new { productId });
        if (qty > stock) qty = stock;
        if (qty == 0) { await RemoveAsync(productId); return (false, "That item just sold out and was removed."); }
        await c.ExecuteAsync("UPDATE dbo.CartItems SET Quantity = @qty WHERE CartKey = @key AND ProductId = @productId", new { qty, key = CartKey, productId });
        return (true, "Cart updated.");
    }

    public async Task RemoveAsync(int productId)
    {
        using var c = _db.Create();
        await c.ExecuteAsync("DELETE FROM dbo.CartItems WHERE CartKey = @key AND ProductId = @productId", new { key = CartKey, productId });
    }

    public async Task ClearAsync()
    {
        using var c = _db.Create();
        await c.ExecuteAsync("DELETE FROM dbo.CartItems WHERE CartKey = @key", new { key = CartKey });
    }
}

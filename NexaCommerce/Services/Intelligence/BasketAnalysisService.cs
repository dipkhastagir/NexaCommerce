using Dapper;
using NexaCommerce.Data;
using NexaCommerce.Models.Intelligence;

namespace NexaCommerce.Services.Intelligence;

/// <summary>
/// Market-basket analysis over completed orders. For every ordered product pair A -> B:
///   support    = orders containing A and B / all orders
///   confidence = orders containing A and B / orders containing A
///   lift       = confidence / (orders containing B / all orders)
/// Lift above 1 means the products are bought together more often than chance.
/// </summary>
public class BasketAnalysisService
{
    private readonly DbConnectionFactory _db;
    private readonly SettingsService _settings;

    public BasketAnalysisService(DbConnectionFactory db, SettingsService settings) { _db = db; _settings = settings; }

    private class PairRow { public int A { get; set; } public int B { get; set; } public int Cnt { get; set; } }
    private class ProductRow { public int ProductId { get; set; } public string Name { get; set; } public decimal Price { get; set; } public string Icon { get; set; } public int Orders { get; set; } public bool IsActive { get; set; } }

    public async Task<List<BasketRule>> RulesAsync(int? minSupportCount = null)
    {
        var minCount = minSupportCount ?? await _settings.GetIntAsync("BasketMinSupportCount", 3);
        using var c = _db.Create();
        var total = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Orders WHERE Status <> 'Cancelled'");
        if (total == 0) return new();

        var pairs = await c.QueryAsync<PairRow>(@"
            SELECT a.ProductId AS A, b.ProductId AS B, COUNT(DISTINCT a.OrderId) AS Cnt
            FROM dbo.OrderItems a
            JOIN dbo.OrderItems b ON b.OrderId = a.OrderId AND b.ProductId <> a.ProductId
            JOIN dbo.Orders o ON o.OrderId = a.OrderId AND o.Status <> 'Cancelled'
            GROUP BY a.ProductId, b.ProductId HAVING COUNT(DISTINCT a.OrderId) >= @minCount", new { minCount });

        var products = (await c.QueryAsync<ProductRow>(@"
            SELECT p.ProductId, p.Name, ISNULL(p.DiscountPrice, p.Price) AS Price, cat.Icon, p.IsActive,
                   (SELECT COUNT(DISTINCT oi.OrderId) FROM dbo.OrderItems oi JOIN dbo.Orders o ON o.OrderId = oi.OrderId
                    WHERE oi.ProductId = p.ProductId AND o.Status <> 'Cancelled') AS Orders
            FROM dbo.Products p JOIN dbo.Categories cat ON cat.CategoryId = p.CategoryId")).ToDictionary(p => p.ProductId);

        var rules = new List<BasketRule>();
        foreach (var p in pairs)
        {
            if (!products.TryGetValue(p.A, out var a) || !products.TryGetValue(p.B, out var b) || a.Orders == 0 || b.Orders == 0) continue;
            var support = (double)p.Cnt / total;
            var confidence = (double)p.Cnt / a.Orders;
            var lift = confidence / ((double)b.Orders / total);
            rules.Add(new BasketRule
            {
                AntecedentId = a.ProductId, AntecedentName = a.Name, ConsequentId = b.ProductId, ConsequentName = b.Name,
                ConsequentPrice = b.Price, ConsequentIcon = b.Icon, PairCount = p.Cnt, Support = support, Confidence = confidence, Lift = lift
            });
        }
        return rules.OrderByDescending(r => r.Lift).ThenByDescending(r => r.Confidence).ToList();
    }

    public async Task<List<BasketRule>> ForProductAsync(int productId, int take = 4)
        => (await RulesAsync()).Where(r => r.AntecedentId == productId && r.Lift > 1).OrderByDescending(r => r.Confidence).Take(take).ToList();

    /// <summary>Suggestions for a whole cart: best rules whose antecedent is in the cart and consequent is not.</summary>
    public async Task<List<BasketRule>> ForCartAsync(IEnumerable<int> cartProductIds, int take = 3)
    {
        var inCart = cartProductIds.ToHashSet();
        if (inCart.Count == 0) return new();
        return (await RulesAsync()).Where(r => inCart.Contains(r.AntecedentId) && !inCart.Contains(r.ConsequentId) && r.Lift > 1)
            .GroupBy(r => r.ConsequentId).Select(g => g.OrderByDescending(r => r.Confidence).First())
            .OrderByDescending(r => r.Confidence * r.Lift).Take(take).ToList();
    }
}

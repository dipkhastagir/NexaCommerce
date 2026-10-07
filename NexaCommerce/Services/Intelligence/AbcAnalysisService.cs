using Dapper;
using NexaCommerce.Data;
using NexaCommerce.Models.Intelligence;

namespace NexaCommerce.Services.Intelligence;

/// <summary>
/// ABC (Pareto) classification by revenue over a window: products making up the first 80% of
/// cumulative revenue are class A, the next 15% class B and the remainder class C.
/// </summary>
public class AbcAnalysisService
{
    private readonly DbConnectionFactory _db;
    public AbcAnalysisService(DbConnectionFactory db) => _db = db;

    public async Task<List<AbcItem>> ClassifyAsync(int days = 90)
    {
        var from = DateTime.Today.AddDays(-days + 1);
        using var c = _db.Create();
        var items = (await c.QueryAsync<AbcItem>(@"
            SELECT p.ProductId, p.Name, p.Sku, cat.Name AS CategoryName,
                   ISNULL(SUM(d.Revenue), 0) AS Revenue, ISNULL(SUM(d.Units), 0) AS Units
            FROM dbo.Products p JOIN dbo.Categories cat ON cat.CategoryId = p.CategoryId
            LEFT JOIN dbo.vw_ProductDailyDemand d ON d.ProductId = p.ProductId AND d.SaleDate >= @from
            WHERE p.IsActive = 1
            GROUP BY p.ProductId, p.Name, p.Sku, cat.Name", new { from })).OrderByDescending(i => i.Revenue).ToList();

        var total = (double)items.Sum(i => i.Revenue);
        double running = 0;
        foreach (var i in items)
        {
            i.Share = total > 0 ? (double)i.Revenue / total : 0;
            var before = running;
            running += i.Share;
            i.Cumulative = running;
            i.Class = before < 0.80 ? "A" : before < 0.95 ? "B" : "C";
        }
        return items;
    }
}

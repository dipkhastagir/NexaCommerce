using Dapper;
using NexaCommerce.Data;
using NexaCommerce.Models.Intelligence;

namespace NexaCommerce.Services.Intelligence;

/// <summary>
/// Inventory policy per product:
///   safety stock  SS  = z * sigma_d * sqrt(L)
///   reorder point ROP = mu_d * L + SS
///   economic order quantity EOQ = sqrt(2 * D * S / H)
/// where mu_d / sigma_d are the mean and standard deviation of daily demand, L the supplier lead time,
/// D annual demand, S the fixed ordering cost and H the annual holding cost per unit.
/// </summary>
public class ReorderService
{
    private readonly DbConnectionFactory _db;
    private readonly ForecastingService _forecast;
    private readonly SettingsService _settings;

    public ReorderService(DbConnectionFactory db, ForecastingService forecast, SettingsService settings)
    {
        _db = db; _forecast = forecast; _settings = settings;
    }

    private class Row
    {
        public int ProductId { get; set; } public string Name { get; set; } public string Sku { get; set; }
        public int StockQuantity { get; set; } public int ReorderLevel { get; set; } public decimal CostPrice { get; set; }
        public int? SupplierId { get; set; } public string SupplierName { get; set; } public int LeadTimeDays { get; set; }
    }

    public async Task<List<ReorderSuggestion>> SuggestAsync()
    {
        var z = await _settings.GetDoubleAsync("ServiceLevelZ", 1.65);
        var orderingCost = await _settings.GetDoubleAsync("OrderingCost", 500);
        var holdingRate = await _settings.GetDoubleAsync("HoldingCostRate", 0.2);
        var series = await _forecast.DailySeriesAsync(60);

        using var c = _db.Create();
        var rows = await c.QueryAsync<Row>(@"SELECT p.ProductId, p.Name, p.Sku, p.StockQuantity, p.ReorderLevel, p.CostPrice, p.SupplierId,
                s.Name AS SupplierName, ISNULL(s.LeadTimeDays, 7) AS LeadTimeDays
            FROM dbo.Products p LEFT JOIN dbo.Suppliers s ON s.SupplierId = p.SupplierId WHERE p.IsActive = 1");

        // Open purchase orders count as stock on the way.
        var onOrder = (await c.QueryAsync<(int ProductId, int Qty)>(@"SELECT i.ProductId, SUM(i.Quantity) FROM dbo.PurchaseOrderItems i
            JOIN dbo.PurchaseOrders po ON po.PurchaseOrderId = i.PurchaseOrderId WHERE po.Status IN ('Draft','Ordered') GROUP BY i.ProductId"))
            .ToDictionary(x => x.ProductId, x => x.Qty);

        var list = new List<ReorderSuggestion>();
        foreach (var r in rows)
        {
            var d = series.TryGetValue(r.ProductId, out var s) ? s.Select(x => x.Units).ToArray() : Array.Empty<double>();
            var mu = d.Length == 0 ? 0 : d.Average();
            var sigma = d.Length > 1 ? Math.Sqrt(d.Select(v => Math.Pow(v - mu, 2)).Sum() / (d.Length - 1)) : 0;
            var L = Math.Max(1, r.LeadTimeDays);
            var ss = z * sigma * Math.Sqrt(L);
            var rop = mu * L + ss;
            var annual = mu * 365;
            var h = Math.Max(1, (double)r.CostPrice * holdingRate);
            var eoq = annual > 0 ? Math.Sqrt(2 * annual * orderingCost / h) : 0;
            var position = r.StockQuantity + onOrder.GetValueOrDefault(r.ProductId);
            var cover = mu > 0 ? r.StockQuantity / mu : double.PositiveInfinity;

            string urgency; int rank;
            if (r.StockQuantity == 0 && mu > 0) { urgency = "Stocked out"; rank = 0; }
            else if (position <= ss) { urgency = "Reorder now"; rank = 1; }
            else if (position <= rop) { urgency = "Reorder now"; rank = 1; }
            else if (position <= rop + mu * 7) { urgency = "Reorder soon"; rank = 2; }
            else { urgency = "Healthy"; rank = 3; }

            var suggested = rank <= 2 ? (int)Math.Ceiling(Math.Max(eoq, rop + ss - position)) : 0;

            list.Add(new ReorderSuggestion
            {
                ProductId = r.ProductId, Name = r.Name, Sku = r.Sku, SupplierId = r.SupplierId, SupplierName = r.SupplierName ?? "-",
                StockQuantity = r.StockQuantity, ReorderLevel = r.ReorderLevel, CostPrice = r.CostPrice, LeadTimeDays = L,
                AvgDailyDemand = mu, StdDailyDemand = sigma, SafetyStock = ss, ReorderPoint = rop, Eoq = eoq,
                DaysOfCover = double.IsInfinity(cover) ? 999 : cover, SuggestedQty = Math.Max(0, suggested),
                Urgency = urgency, UrgencyRank = rank
            });
        }
        return list.OrderBy(x => x.UrgencyRank).ThenBy(x => x.DaysOfCover).ToList();
    }
}

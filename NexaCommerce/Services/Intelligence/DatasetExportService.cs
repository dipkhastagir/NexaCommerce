using System.Text;
using Dapper;
using NexaCommerce.Data;
using NexaCommerce.Infrastructure;
using NexaCommerce.Models.Intelligence;

namespace NexaCommerce.Services.Intelligence;

/// <summary>
/// Exports anonymised, analysis-ready CSV datasets so the platform's data can be used in
/// notebooks (Python / R) for research. Customer identities are replaced with stable pseudonyms.
/// </summary>
public class DatasetExportService
{
    private readonly DbConnectionFactory _db;
    private readonly SegmentationService _rfm;
    private readonly ForecastingService _forecast;

    public DatasetExportService(DbConnectionFactory db, SegmentationService rfm, ForecastingService forecast)
    {
        _db = db; _rfm = rfm; _forecast = forecast;
    }

    public static readonly DatasetInfo[] Catalog =
    {
        new() { Key = "orders", Title = "Orders", Description = "One row per order with value, payment, status and risk label.", Columns = "order_id, customer, created_at, weekday, hour, items, subtotal, discount, shipping, total, payment_method, status, city, risk_score, risk_level" },
        new() { Key = "order-lines", Title = "Order lines (transactions)", Description = "One row per product in an order. Use for basket mining (Apriori, FP-Growth).", Columns = "order_id, product_id, sku, category, quantity, unit_price, line_total" },
        new() { Key = "daily-demand", Title = "Daily demand panel", Description = "Zero-filled daily unit sales per product for the last 120 days. Use for forecasting benchmarks.", Columns = "date, product_id, sku, units" },
        new() { Key = "rfm", Title = "Customer RFM features", Description = "Recency, frequency, monetary values with quintile scores and segment label.", Columns = "customer, recency_days, frequency, monetary, r, f, m, segment" },
        new() { Key = "products", Title = "Product catalog", Description = "Price, cost, stock and review statistics per product.", Columns = "product_id, sku, name, category, brand, price, cost, sale_price, stock, reorder_level, avg_rating, reviews, units_sold" },
        new() { Key = "stock-movements", Title = "Stock ledger", Description = "Every stock movement with signed quantity and running balance.", Columns = "movement_id, product_id, sku, type, quantity, balance_after, created_at" }
    };

    private static string Pseudonym(int userId) => "C" + ((userId * 7919L) % 100000).ToString("D5");

    public async Task<int> CountAsync(string key)
    {
        using var c = _db.Create();
        return key switch
        {
            "orders" => await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Orders"),
            "order-lines" => await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.OrderItems"),
            "daily-demand" => await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Products WHERE IsActive = 1") * 120,
            "rfm" => await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.vw_CustomerRfmBase"),
            "products" => await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Products"),
            "stock-movements" => await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.StockMovements"),
            _ => 0
        };
    }

    private class OrderRow
    {
        public int OrderId { get; set; } public int UserId { get; set; } public DateTime CreatedAt { get; set; } public int ItemCount { get; set; }
        public decimal SubTotal { get; set; } public decimal DiscountAmount { get; set; } public decimal ShippingFee { get; set; } public decimal TotalAmount { get; set; }
        public string PaymentMethod { get; set; } public string Status { get; set; } public string ShipCity { get; set; } public int RiskScore { get; set; } public string RiskLevel { get; set; }
    }
    private class LineRow { public int OrderId { get; set; } public int ProductId { get; set; } public string Sku { get; set; } public string Category { get; set; } public int Quantity { get; set; } public decimal UnitPrice { get; set; } public decimal LineTotal { get; set; } }
    private class ProductRow { public int ProductId { get; set; } public string Sku { get; set; } public string Name { get; set; } public string CategoryName { get; set; } public string BrandName { get; set; } public decimal Price { get; set; } public decimal CostPrice { get; set; } public decimal? DiscountPrice { get; set; } public int StockQuantity { get; set; } public int ReorderLevel { get; set; } public decimal AvgRating { get; set; } public int ReviewCount { get; set; } public int UnitsSold { get; set; } }
    private class MoveRow { public int MovementId { get; set; } public int ProductId { get; set; } public string Sku { get; set; } public string MovementType { get; set; } public int Quantity { get; set; } public int BalanceAfter { get; set; } public DateTime CreatedAt { get; set; } }

    public async Task<string> BuildCsvAsync(string key)
    {
        var sb = new StringBuilder();
        void Row(params object[] values) => sb.AppendLine(string.Join(",", values.Select(CsvWriter.Escape)));
        using var c = _db.Create();

        switch (key)
        {
            case "orders":
                Row("order_id", "customer", "created_at", "weekday", "hour", "items", "subtotal", "discount", "shipping", "total", "payment_method", "status", "city", "risk_score", "risk_level");
                foreach (var o in await c.QueryAsync<OrderRow>("SELECT * FROM dbo.vw_OrderSummary ORDER BY OrderId"))
                    Row(o.OrderId, Pseudonym(o.UserId), o.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"), o.CreatedAt.DayOfWeek, o.CreatedAt.Hour, o.ItemCount,
                        o.SubTotal, o.DiscountAmount, o.ShippingFee, o.TotalAmount, o.PaymentMethod, o.Status, o.ShipCity, o.RiskScore, o.RiskLevel);
                break;
            case "order-lines":
                Row("order_id", "product_id", "sku", "category", "quantity", "unit_price", "line_total");
                foreach (var l in await c.QueryAsync<LineRow>(@"SELECT oi.OrderId, oi.ProductId, p.Sku, cat.Name AS Category, oi.Quantity, oi.UnitPrice, oi.LineTotal
                    FROM dbo.OrderItems oi JOIN dbo.Products p ON p.ProductId = oi.ProductId JOIN dbo.Categories cat ON cat.CategoryId = p.CategoryId ORDER BY oi.OrderId"))
                    Row(l.OrderId, l.ProductId, l.Sku, l.Category, l.Quantity, l.UnitPrice, l.LineTotal);
                break;
            case "daily-demand":
                Row("date", "product_id", "sku", "units");
                var skus = (await c.QueryAsync<(int Id, string Sku)>("SELECT ProductId, Sku FROM dbo.Products")).ToDictionary(x => x.Id, x => x.Sku);
                foreach (var (pid, series) in await _forecast.DailySeriesAsync(120))
                    foreach (var p in series) Row(p.Date.ToString("yyyy-MM-dd"), pid, skus.GetValueOrDefault(pid), p.Units);
                break;
            case "rfm":
                Row("customer", "recency_days", "frequency", "monetary", "r", "f", "m", "segment");
                foreach (var r in await _rfm.ScoreAsync())
                    Row(Pseudonym(r.UserId), r.RecencyDays, r.Frequency, r.Monetary, r.R, r.F, r.M, r.Segment);
                break;
            case "products":
                Row("product_id", "sku", "name", "category", "brand", "price", "cost", "sale_price", "stock", "reorder_level", "avg_rating", "reviews", "units_sold");
                foreach (var p in await c.QueryAsync<ProductRow>("SELECT * FROM dbo.vw_ProductList ORDER BY ProductId"))
                    Row(p.ProductId, p.Sku, p.Name, p.CategoryName, p.BrandName, p.Price, p.CostPrice, p.DiscountPrice, p.StockQuantity, p.ReorderLevel, p.AvgRating, p.ReviewCount, p.UnitsSold);
                break;
            case "stock-movements":
                Row("movement_id", "product_id", "sku", "type", "quantity", "balance_after", "created_at");
                foreach (var m in await c.QueryAsync<MoveRow>("SELECT m.MovementId, m.ProductId, p.Sku, m.MovementType, m.Quantity, m.BalanceAfter, m.CreatedAt FROM dbo.StockMovements m JOIN dbo.Products p ON p.ProductId = m.ProductId ORDER BY m.MovementId"))
                    Row(m.MovementId, m.ProductId, m.Sku, m.MovementType, m.Quantity, m.BalanceAfter, m.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                break;
            default:
                return null;
        }
        return sb.ToString();
    }
}

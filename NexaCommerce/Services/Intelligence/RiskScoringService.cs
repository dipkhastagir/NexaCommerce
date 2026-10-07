using Dapper;
using Microsoft.Data.SqlClient;
using NexaCommerce.Data;
using NexaCommerce.Models.Intelligence;

namespace NexaCommerce.Services.Intelligence;

/// <summary>
/// Transparent, rule-based order risk model. Each signal adds points and a human-readable reason so
/// reviewers can see why an order was flagged. Thresholds are configurable in Settings.
/// Signals: new account, value far above the customer's own history, value z-score against all orders,
/// unusually large line quantity, order velocity in the last 24 h, ship-to city different from the
/// customer's default address, high-value cash-on-delivery and coupon use on a first order.
/// </summary>
public class RiskScoringService
{
    private readonly DbConnectionFactory _db;
    private readonly SettingsService _settings;

    public RiskScoringService(DbConnectionFactory db, SettingsService settings) { _db = db; _settings = settings; }

    private const string FeatureSql = @"
        SELECT o.OrderId, o.UserId, o.TotalAmount, o.CreatedAt, u.CreatedAt AS UserCreatedAt,
               (SELECT MAX(Quantity) FROM dbo.OrderItems WHERE OrderId = o.OrderId) AS MaxLineQty,
               (SELECT COUNT(*) FROM dbo.OrderItems WHERE OrderId = o.OrderId) AS DistinctItems,
               o.ShipCity, (SELECT TOP 1 City FROM dbo.Addresses a WHERE a.UserId = o.UserId AND a.IsDefault = 1) AS DefaultCity,
               o.PaymentMethod, o.CouponCode
        FROM dbo.Orders o JOIN dbo.Users u ON u.UserId = o.UserId";

    public async Task<(int Score, string Level, string Reasons)> ScoreOrderAsync(int orderId)
    {
        using var c = _db.Create();
        var target = await c.QueryFirstOrDefaultAsync<OrderRiskFeatures>(FeatureSql + " WHERE o.OrderId = @orderId", new { orderId });
        if (target == null) return (0, "Low", "");
        var prior = (await c.QueryAsync<OrderRiskFeatures>(FeatureSql + " WHERE o.UserId = @UserId AND o.OrderId < @orderId AND o.Status <> 'Cancelled'",
            new { target.UserId, orderId })).ToList();
        var stats = await c.QueryFirstAsync<(decimal Mean, double Std)>(
            "SELECT ISNULL(AVG(TotalAmount), 0), ISNULL(STDEV(TotalAmount), 0) FROM dbo.Orders WHERE Status <> 'Cancelled'");
        var (high, medium) = await ThresholdsAsync();
        var (score, reasons) = Compute(target, prior, (double)stats.Mean, stats.Std);
        var level = Level(score, high, medium);
        await c.ExecuteAsync("UPDATE dbo.Orders SET RiskScore = @score, RiskLevel = @level, RiskReasons = @reasons WHERE OrderId = @orderId",
            new { score, level, reasons, orderId });
        return (score, level, reasons);
    }

    /// <summary>Re-scores every order in chronological order. Used after seeding and from the Intelligence page.</summary>
    public async Task<int> RescoreAllAsync(string connectionString = null)
    {
        await using var c = connectionString != null ? new SqlConnection(connectionString) : _db.Create();
        await c.OpenAsync();
        var all = (await c.QueryAsync<OrderRiskFeatures>(FeatureSql + " ORDER BY o.OrderId")).ToList();
        if (all.Count == 0) return 0;
        var totals = all.Select(a => (double)a.TotalAmount).ToArray();
        var mean = totals.Average();
        var std = totals.Length > 1 ? Math.Sqrt(totals.Select(t => Math.Pow(t - mean, 2)).Sum() / (totals.Length - 1)) : 0;

        int high = 60, medium = 30;
        var settings = (await c.QueryAsync<(string Key, string Value)>("SELECT SettingKey, SettingValue FROM dbo.Settings WHERE SettingKey IN ('RiskHighThreshold','RiskMediumThreshold')"))
            .ToDictionary(x => x.Key, x => x.Value);
        if (settings.TryGetValue("RiskHighThreshold", out var h) && int.TryParse(h, out var hv)) high = hv;
        if (settings.TryGetValue("RiskMediumThreshold", out var m) && int.TryParse(m, out var mv)) medium = mv;

        var history = new Dictionary<int, List<OrderRiskFeatures>>();
        using var tx = c.BeginTransaction();
        foreach (var o in all)
        {
            var prior = history.TryGetValue(o.UserId, out var list) ? list : new List<OrderRiskFeatures>();
            var (score, reasons) = Compute(o, prior, mean, std);
            await c.ExecuteAsync("UPDATE dbo.Orders SET RiskScore = @score, RiskLevel = @level, RiskReasons = @reasons WHERE OrderId = @OrderId",
                new { score, level = Level(score, high, medium), reasons, o.OrderId }, tx);
            prior.Add(o);
            history[o.UserId] = prior;
        }
        tx.Commit();
        return all.Count;
    }

    public async Task<List<OrderRiskRow>> FlaggedAsync(string level)
    {
        using var c = _db.Create();
        return (await c.QueryAsync<OrderRiskRow>(@"SELECT TOP 200 OrderId, OrderNumber, CustomerName, TotalAmount, Status, PaymentMethod, RiskScore, RiskLevel, RiskReasons, CreatedAt
            FROM dbo.vw_OrderSummary WHERE (@level IS NULL AND RiskLevel IN ('High','Medium')) OR RiskLevel = @level
            ORDER BY RiskScore DESC, OrderId DESC", new { level = string.IsNullOrWhiteSpace(level) ? null : level })).ToList();
    }

    public async Task<List<(int Bucket, int Count)>> DistributionAsync()
    {
        using var c = _db.Create();
        return (await c.QueryAsync<(int Bucket, int Count)>("SELECT (RiskScore / 10) * 10 AS Bucket, COUNT(*) FROM dbo.Orders GROUP BY (RiskScore / 10) * 10 ORDER BY Bucket")).ToList();
    }

    private async Task<(int High, int Medium)> ThresholdsAsync()
        => (await _settings.GetIntAsync("RiskHighThreshold", 60), await _settings.GetIntAsync("RiskMediumThreshold", 30));

    private static string Level(int score, int high, int medium) => score >= high ? "High" : score >= medium ? "Medium" : "Low";

    public static (int Score, string Reasons) Compute(OrderRiskFeatures o, List<OrderRiskFeatures> prior, double globalMean, double globalStd)
    {
        int score = 0;
        var reasons = new List<string>();
        var total = (double)o.TotalAmount;

        var accountAge = (o.CreatedAt - o.UserCreatedAt).TotalDays;
        if (accountAge < 1) { score += 20; reasons.Add("account created the same day"); }
        else if (accountAge < 7) { score += 10; reasons.Add("account under a week old"); }

        if (prior.Count >= 2)
        {
            var avg = prior.Average(p => (double)p.TotalAmount);
            if (avg > 0 && total > avg * 3) { score += 25; reasons.Add($"{total / avg:F1}x this customer's average order"); }
        }
        else if (prior.Count == 0 && globalStd > 0 && total > globalMean + 2 * globalStd)
        {
            score += 15; reasons.Add("large first order");
        }

        if (globalStd > 0)
        {
            var z = (total - globalMean) / globalStd;
            if (z > 3) { score += 25; reasons.Add($"order value z-score {z:F1}"); }
            else if (z > 2) { score += 12; reasons.Add($"order value z-score {z:F1}"); }
        }

        if (o.MaxLineQty >= 8) { score += 25; reasons.Add($"{o.MaxLineQty} units of one item"); }
        else if (o.MaxLineQty >= 5) { score += 15; reasons.Add($"{o.MaxLineQty} units of one item"); }

        var recent = prior.Count(p => (o.CreatedAt - p.CreatedAt).TotalHours is >= 0 and < 24);
        if (recent >= 2) { score += 20; reasons.Add($"{recent + 1} orders within 24 hours"); }

        if (!string.IsNullOrWhiteSpace(o.DefaultCity) && !string.Equals(o.DefaultCity, o.ShipCity, StringComparison.OrdinalIgnoreCase))
        { score += 8; reasons.Add($"ships to {o.ShipCity}, home city {o.DefaultCity}"); }

        if (o.PaymentMethod == "COD" && globalStd > 0 && total > globalMean + 1.5 * globalStd)
        { score += 10; reasons.Add("high-value cash on delivery"); }

        if (!string.IsNullOrEmpty(o.CouponCode) && prior.Count == 0) { score += 5; reasons.Add("coupon on first order"); }

        return (Math.Min(100, score), string.Join("; ", reasons));
    }
}

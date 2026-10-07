using Dapper;
using NexaCommerce.Data;
using NexaCommerce.Models.Intelligence;

namespace NexaCommerce.Services.Intelligence;

/// <summary>
/// Per-product demand forecasting with Holt's linear (double) exponential smoothing.
/// Smoothing constants are chosen by grid search on one-step-ahead squared error, accuracy is
/// reported on a 14-day hold-out (MAE, RMSE, sMAPE) and compared with a naive baseline.
/// </summary>
public class ForecastingService
{
    private readonly DbConnectionFactory _db;
    private readonly SettingsService _settings;

    public ForecastingService(DbConnectionFactory db, SettingsService settings) { _db = db; _settings = settings; }

    private class ProductRow { public int ProductId { get; set; } public string Name { get; set; } public string Sku { get; set; } public string CategoryName { get; set; } public int StockQuantity { get; set; } }
    private class DemandRow { public int ProductId { get; set; } public DateTime SaleDate { get; set; } public int Units { get; set; } }

    /// <summary>Daily unit series for every active product over the last <paramref name="days"/> days (zero-filled).</summary>
    public async Task<Dictionary<int, List<DemandPoint>>> DailySeriesAsync(int days)
    {
        var from = DateTime.Today.AddDays(-days + 1);
        using var c = _db.Create();
        var rows = await c.QueryAsync<DemandRow>("SELECT ProductId, SaleDate, Units FROM dbo.vw_ProductDailyDemand WHERE SaleDate >= @from", new { from });
        var ids = await c.QueryAsync<int>("SELECT ProductId FROM dbo.Products WHERE IsActive = 1");
        var lookup = rows.GroupBy(r => r.ProductId).ToDictionary(g => g.Key, g => g.ToDictionary(r => r.SaleDate.Date, r => r.Units));
        var result = new Dictionary<int, List<DemandPoint>>();
        foreach (var id in ids)
        {
            var series = new List<DemandPoint>(days);
            lookup.TryGetValue(id, out var map);
            for (var d = from; d <= DateTime.Today; d = d.AddDays(1))
                series.Add(new DemandPoint { Date = d, Units = map != null && map.TryGetValue(d, out var u) ? u : 0 });
            result[id] = series;
        }
        return result;
    }

    public async Task<List<ForecastResult>> ForecastAllAsync()
    {
        var historyDays = await _settings.GetIntAsync("ForecastHistoryDays", 120);
        var horizon = await _settings.GetIntAsync("ForecastHorizonDays", 30);
        var series = await DailySeriesAsync(historyDays);
        using var c = _db.Create();
        var products = (await c.QueryAsync<ProductRow>(@"SELECT p.ProductId, p.Name, p.Sku, c.Name AS CategoryName, p.StockQuantity
            FROM dbo.Products p JOIN dbo.Categories c ON c.CategoryId = p.CategoryId WHERE p.IsActive = 1")).ToList();
        var list = new List<ForecastResult>();
        foreach (var p in products)
        {
            if (!series.TryGetValue(p.ProductId, out var s)) continue;
            var r = Fit(s, horizon);
            r.ProductId = p.ProductId; r.Name = p.Name; r.Sku = p.Sku; r.CategoryName = p.CategoryName; r.StockQuantity = p.StockQuantity;
            r.DaysUntilStockout = StockoutDay(r.Forecast, p.StockQuantity);
            list.Add(r);
        }
        return list.OrderByDescending(r => r.ForecastTotal).ToList();
    }

    public async Task<ForecastResult> ForecastProductAsync(int productId)
    {
        return (await ForecastAllAsync()).FirstOrDefault(r => r.ProductId == productId);
    }

    /// <summary>Fits Holt's method to a daily series and projects <paramref name="horizon"/> days.</summary>
    public static ForecastResult Fit(List<DemandPoint> history, int horizon)
    {
        var y = history.Select(h => h.Units).ToArray();
        var result = new ForecastResult { History = history, AvgDailyDemand = y.Length == 0 ? 0 : y.Average() };
        if (y.Length < 10)
        {
            result.Forecast = Enumerable.Range(1, horizon).Select(i => new DemandPoint { Date = DateTime.Today.AddDays(i), Units = result.AvgDailyDemand }).ToList();
            result.ForecastTotal = result.AvgDailyDemand * horizon;
            return result;
        }

        // Hold out the last 14 days to measure out-of-sample accuracy.
        int holdout = Math.Min(14, y.Length / 4);
        var train = y.Take(y.Length - holdout).ToArray();
        var test = y.Skip(y.Length - holdout).ToArray();

        double bestA = 0.3, bestB = 0.1, bestErr = double.MaxValue;
        for (double a = 0.05; a <= 0.951; a += 0.05)
            for (double b = 0.01; b <= 0.51; b += 0.05)
            {
                var err = OneStepSse(train, a, b);
                if (err < bestErr) { bestErr = err; bestA = a; bestB = b; }
            }

        // Accuracy on the hold-out using parameters from the training window.
        var (lt, tt, _) = Run(train, bestA, bestB);
        double mae = 0, mse = 0, smape = 0, naive = 0;
        for (int h = 1; h <= test.Length; h++)
        {
            var f = Math.Max(0, lt + h * tt);
            var actual = test[h - 1];
            mae += Math.Abs(actual - f);
            mse += Math.Pow(actual - f, 2);
            var denom = Math.Abs(actual) + Math.Abs(f);
            smape += denom == 0 ? 0 : 2 * Math.Abs(actual - f) / denom;
            naive += Math.Abs(actual - train[^1]);
        }
        result.Mae = mae / test.Length;
        result.Rmse = Math.Sqrt(mse / test.Length);
        result.Smape = smape / test.Length;
        result.NaiveMae = naive / test.Length;

        // Refit on the full series for the actual forecast.
        var (level, trend, fitted) = Run(y, bestA, bestB);
        result.Alpha = Math.Round(bestA, 2);
        result.Beta = Math.Round(bestB, 2);
        result.Level = level;
        result.Trend = trend;
        result.Fitted = history.Select((h, i) => new DemandPoint { Date = h.Date, Units = Math.Max(0, fitted[i]) }).ToList();

        var residuals = y.Select((v, i) => v - fitted[i]).Skip(1).ToArray();
        var sigma = residuals.Length > 1 ? Math.Sqrt(residuals.Select(r => r * r).Sum() / (residuals.Length - 1)) : 0;
        for (int h = 1; h <= horizon; h++)
        {
            var f = Math.Max(0, level + h * trend);
            var band = 1.96 * sigma * Math.Sqrt(1 + (h - 1) * bestA * bestA);
            var date = DateTime.Today.AddDays(h);
            result.Forecast.Add(new DemandPoint { Date = date, Units = f });
            result.Lower.Add(new DemandPoint { Date = date, Units = Math.Max(0, f - band) });
            result.Upper.Add(new DemandPoint { Date = date, Units = f + band });
        }
        result.ForecastTotal = result.Forecast.Sum(f => f.Units);
        return result;
    }

    private static double OneStepSse(double[] y, double a, double b)
    {
        double level = y[0], trend = y.Length > 1 ? y[1] - y[0] : 0, sse = 0;
        for (int t = 1; t < y.Length; t++)
        {
            var forecast = level + trend;
            sse += Math.Pow(y[t] - forecast, 2);
            var prevLevel = level;
            level = a * y[t] + (1 - a) * (level + trend);
            trend = b * (level - prevLevel) + (1 - b) * trend;
        }
        return sse;
    }

    private static (double Level, double Trend, double[] Fitted) Run(double[] y, double a, double b)
    {
        var fitted = new double[y.Length];
        double level = y[0], trend = y.Length > 1 ? (y.Skip(1).Take(7).DefaultIfEmpty(y[0]).Average() - y[0]) / 7.0 : 0;
        fitted[0] = y[0];
        for (int t = 1; t < y.Length; t++)
        {
            fitted[t] = level + trend;
            var prevLevel = level;
            level = a * y[t] + (1 - a) * (level + trend);
            trend = b * (level - prevLevel) + (1 - b) * trend;
        }
        return (level, trend, fitted);
    }

    private static double? StockoutDay(List<DemandPoint> forecast, int stock)
    {
        double remaining = stock;
        for (int i = 0; i < forecast.Count; i++)
        {
            remaining -= forecast[i].Units;
            if (remaining <= 0) return i + 1;
        }
        return null;
    }
}

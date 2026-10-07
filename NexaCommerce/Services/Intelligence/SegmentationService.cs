using Dapper;
using NexaCommerce.Data;
using NexaCommerce.Models.Intelligence;

namespace NexaCommerce.Services.Intelligence;

/// <summary>
/// RFM (Recency, Frequency, Monetary) segmentation. Each dimension is scored 1-5 by quintile across
/// active customers, then the R and F scores map to named segments with a recommended action.
/// </summary>
public class SegmentationService
{
    private readonly DbConnectionFactory _db;
    public SegmentationService(DbConnectionFactory db) => _db = db;

    private class BaseRow
    {
        public int UserId { get; set; } public string FullName { get; set; } public string Email { get; set; }
        public DateTime? LastOrderDate { get; set; } public int Frequency { get; set; } public decimal Monetary { get; set; }
    }

    public static readonly Dictionary<string, (string Action, string Color)> SegmentInfo = new()
    {
        ["Champions"] = ("Reward them: early access and referral perks", "#1F7A55"),
        ["Loyal"] = ("Upsell higher-margin items and bundles", "#2457B3"),
        ["Potential loyalists"] = ("Offer a membership or second-order incentive", "#1C7487"),
        ["New customers"] = ("Onboard with a welcome series and how-to content", "#4B2FD6"),
        ["Promising"] = ("Build awareness with personalised picks", "#6B3FA0"),
        ["Need attention"] = ("Send time-limited offers based on past purchases", "#B4571B"),
        ["At risk"] = ("Win-back campaign before they lapse", "#B32D63"),
        ["Can't lose them"] = ("Personal outreach and a strong win-back offer", "#9E1F3A"),
        ["Hibernating"] = ("Low-cost reactivation or let go", "#6D6A1E"),
        ["No purchases"] = ("Prompt a first order with a starter coupon", "#77748A")
    };

    public async Task<List<RfmCustomer>> ScoreAsync()
    {
        using var c = _db.Create();
        var rows = (await c.QueryAsync<BaseRow>("SELECT UserId, FullName, Email, LastOrderDate, Frequency, Monetary FROM dbo.vw_CustomerRfmBase")).ToList();
        var today = DateTime.Today;
        var buyers = rows.Where(r => r.Frequency > 0).ToList();

        var recencies = buyers.Select(b => (today - b.LastOrderDate.Value.Date).TotalDays).OrderBy(x => x).ToArray();
        var freqs = buyers.Select(b => (double)b.Frequency).OrderBy(x => x).ToArray();
        var money = buyers.Select(b => (double)b.Monetary).OrderBy(x => x).ToArray();

        var result = new List<RfmCustomer>();
        foreach (var r in rows)
        {
            var c1 = new RfmCustomer { UserId = r.UserId, FullName = r.FullName, Email = r.Email, LastOrderDate = r.LastOrderDate, Frequency = r.Frequency, Monetary = r.Monetary };
            if (r.Frequency == 0)
            {
                c1.RecencyDays = -1; c1.Segment = "No purchases";
                result.Add(c1); continue;
            }
            c1.RecencyDays = (int)(today - r.LastOrderDate.Value.Date).TotalDays;
            c1.R = 6 - Quintile(recencies, c1.RecencyDays);      // fewer days since last order = better
            c1.F = Quintile(freqs, r.Frequency);
            c1.M = Quintile(money, (double)r.Monetary);
            c1.Segment = Classify(c1.R, c1.F);
            result.Add(c1);
        }
        return result.OrderByDescending(x => x.Monetary).ToList();
    }

    public async Task<List<SegmentSummary>> SummaryAsync(List<RfmCustomer> scored = null)
    {
        scored ??= await ScoreAsync();
        return scored.GroupBy(s => s.Segment).Select(g => new SegmentSummary
        {
            Segment = g.Key,
            Customers = g.Count(),
            Revenue = g.Sum(x => x.Monetary),
            AvgRecency = g.Where(x => x.RecencyDays >= 0).Select(x => (double)x.RecencyDays).DefaultIfEmpty(0).Average(),
            AvgFrequency = g.Average(x => (double)x.Frequency),
            Action = SegmentInfo.TryGetValue(g.Key, out var i) ? i.Action : "",
            Color = SegmentInfo.TryGetValue(g.Key, out var j) ? j.Color : "#77748A"
        }).OrderByDescending(s => s.Revenue).ToList();
    }

    /// <summary>Score 1-5 from the value's position in a sorted array (ties share the lower bucket).</summary>
    private static int Quintile(double[] sorted, double value)
    {
        if (sorted.Length == 0) return 1;
        int below = 0;
        while (below < sorted.Length && sorted[below] < value) below++;
        var pct = (double)below / sorted.Length;
        return Math.Clamp((int)Math.Floor(pct * 5) + 1, 1, 5);
    }

    private static string Classify(int r, int f)
    {
        if (r >= 4 && f >= 4) return "Champions";
        if (r >= 3 && f >= 4) return "Loyal";
        if (r >= 4 && f >= 2) return "Potential loyalists";
        if (r == 5 && f == 1) return "New customers";
        if (r == 4 && f == 1) return "Promising";
        if (r == 3 && f <= 3) return "Need attention";
        if (r <= 2 && f >= 4) return "Can't lose them";
        if (r <= 2 && f >= 2) return "At risk";
        return "Hibernating";
    }
}

namespace NexaCommerce.Models.Intelligence;

/// <summary>One day of unit demand for a product.</summary>
public class DemandPoint
{
    public DateTime Date { get; set; }
    public double Units { get; set; }
}

/// <summary>Holt (double exponential smoothing) forecast for one product.</summary>
public class ForecastResult
{
    public int ProductId { get; set; }
    public string Name { get; set; }
    public string Sku { get; set; }
    public string CategoryName { get; set; }
    public int StockQuantity { get; set; }
    public List<DemandPoint> History { get; set; } = new();
    public List<DemandPoint> Fitted { get; set; } = new();
    public List<DemandPoint> Forecast { get; set; } = new();
    public List<DemandPoint> Lower { get; set; } = new();
    public List<DemandPoint> Upper { get; set; } = new();
    public double Alpha { get; set; }
    public double Beta { get; set; }
    public double Level { get; set; }
    public double Trend { get; set; }
    public double Mae { get; set; }
    public double Rmse { get; set; }
    public double Smape { get; set; }
    public double NaiveMae { get; set; }
    public double AvgDailyDemand { get; set; }
    public double ForecastTotal { get; set; }
    public double? DaysUntilStockout { get; set; }
    public string TrendLabel => Trend > 0.02 ? "Rising" : Trend < -0.02 ? "Falling" : "Stable";
    /// <summary>MASE-style ratio: below 1 means the model beats a naive "same as yesterday" forecast.</summary>
    public double SkillRatio => NaiveMae > 0 ? Mae / NaiveMae : 0;
}

public class ReorderSuggestion
{
    public int ProductId { get; set; }
    public string Name { get; set; }
    public string Sku { get; set; }
    public string SupplierName { get; set; }
    public int? SupplierId { get; set; }
    public int StockQuantity { get; set; }
    public int ReorderLevel { get; set; }
    public decimal CostPrice { get; set; }
    public int LeadTimeDays { get; set; }
    public double AvgDailyDemand { get; set; }
    public double StdDailyDemand { get; set; }
    public double SafetyStock { get; set; }
    public double ReorderPoint { get; set; }
    public double Eoq { get; set; }
    public double DaysOfCover { get; set; }
    public int SuggestedQty { get; set; }
    public string Urgency { get; set; }
    public int UrgencyRank { get; set; }
}

public class RfmCustomer
{
    public int UserId { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
    public DateTime? LastOrderDate { get; set; }
    public int RecencyDays { get; set; }
    public int Frequency { get; set; }
    public decimal Monetary { get; set; }
    public int R { get; set; }
    public int F { get; set; }
    public int M { get; set; }
    public string Score => $"{R}{F}{M}";
    public string Segment { get; set; }
}

public class SegmentSummary
{
    public string Segment { get; set; }
    public int Customers { get; set; }
    public decimal Revenue { get; set; }
    public double AvgRecency { get; set; }
    public double AvgFrequency { get; set; }
    public string Action { get; set; }
    public string Color { get; set; }
}

public class BasketRule
{
    public int AntecedentId { get; set; }
    public string AntecedentName { get; set; }
    public int ConsequentId { get; set; }
    public string ConsequentName { get; set; }
    public decimal ConsequentPrice { get; set; }
    public string ConsequentIcon { get; set; }
    public int PairCount { get; set; }
    public double Support { get; set; }
    public double Confidence { get; set; }
    public double Lift { get; set; }
}

public class AbcItem
{
    public int ProductId { get; set; }
    public string Name { get; set; }
    public string Sku { get; set; }
    public string CategoryName { get; set; }
    public decimal Revenue { get; set; }
    public int Units { get; set; }
    public double Share { get; set; }
    public double Cumulative { get; set; }
    public string Class { get; set; }
}

public class OrderRiskRow
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; }
    public string CustomerName { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; }
    public string PaymentMethod { get; set; }
    public int RiskScore { get; set; }
    public string RiskLevel { get; set; }
    public string RiskReasons { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Raw features the risk model needs for one order.</summary>
public class OrderRiskFeatures
{
    public int OrderId { get; set; }
    public int UserId { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UserCreatedAt { get; set; }
    public int MaxLineQty { get; set; }
    public int DistinctItems { get; set; }
    public string ShipCity { get; set; }
    public string DefaultCity { get; set; }
    public string PaymentMethod { get; set; }
    public string CouponCode { get; set; }
}

public class IntelligenceHubVm
{
    public int ProductsForecast { get; set; }
    public int ReorderNow { get; set; }
    public int ReorderSoon { get; set; }
    public int Champions { get; set; }
    public int AtRisk { get; set; }
    public int RulesFound { get; set; }
    public double BestLift { get; set; }
    public int HighRisk { get; set; }
    public int MediumRisk { get; set; }
    public int ClassA { get; set; }
    public double ClassARevenueShare { get; set; }
    public double MedianSkill { get; set; }
    public List<ReorderSuggestion> UrgentReorders { get; set; } = new();
    public List<SegmentSummary> Segments { get; set; } = new();
}

public class ForecastIndexVm
{
    public List<ForecastResult> Results { get; set; } = new();
    public int Horizon { get; set; }
    public int HistoryDays { get; set; }
}

public class SegmentsVm
{
    public List<RfmCustomer> Customers { get; set; } = new();
    public List<SegmentSummary> Summary { get; set; } = new();
    public string Filter { get; set; }
}

public class DatasetInfo
{
    public string Key { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string Columns { get; set; }
    public int Rows { get; set; }
}

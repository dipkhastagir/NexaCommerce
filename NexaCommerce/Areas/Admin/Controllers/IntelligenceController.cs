using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaCommerce.Infrastructure;
using NexaCommerce.Models.Intelligence;
using NexaCommerce.Services;
using NexaCommerce.Services.Intelligence;

namespace NexaCommerce.Areas.Admin.Controllers;

[Authorize(Policy = "Management")]
public class IntelligenceController : AdminBaseController
{
    private readonly ForecastingService _forecast;
    private readonly ReorderService _reorder;
    private readonly SegmentationService _rfm;
    private readonly BasketAnalysisService _basket;
    private readonly RiskScoringService _risk;
    private readonly AbcAnalysisService _abc;
    private readonly DatasetExportService _export;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;

    public IntelligenceController(ForecastingService forecast, ReorderService reorder, SegmentationService rfm, BasketAnalysisService basket,
        RiskScoringService risk, AbcAnalysisService abc, DatasetExportService export, SettingsService settings, AuditService audit)
    {
        _forecast = forecast; _reorder = reorder; _rfm = rfm; _basket = basket; _risk = risk; _abc = abc; _export = export; _settings = settings; _audit = audit;
    }

    public async Task<IActionResult> Index()
    {
        var reorder = await _reorder.SuggestAsync();
        var rfm = await _rfm.ScoreAsync();
        var rules = await _basket.RulesAsync();
        var flagged = await _risk.FlaggedAsync(null);
        var abc = await _abc.ClassifyAsync();
        var forecasts = await _forecast.ForecastAllAsync();
        var skills = forecasts.Where(f => f.NaiveMae > 0).Select(f => f.SkillRatio).OrderBy(x => x).ToList();
        var vm = new IntelligenceHubVm
        {
            ProductsForecast = forecasts.Count,
            ReorderNow = reorder.Count(r => r.UrgencyRank <= 1),
            ReorderSoon = reorder.Count(r => r.UrgencyRank == 2),
            Champions = rfm.Count(r => r.Segment == "Champions"),
            AtRisk = rfm.Count(r => r.Segment is "At risk" or "Can't lose them"),
            RulesFound = rules.Count,
            BestLift = rules.Count > 0 ? rules.Max(r => r.Lift) : 0,
            HighRisk = flagged.Count(f => f.RiskLevel == "High"),
            MediumRisk = flagged.Count(f => f.RiskLevel == "Medium"),
            ClassA = abc.Count(a => a.Class == "A"),
            ClassARevenueShare = abc.Where(a => a.Class == "A").Sum(a => a.Share),
            MedianSkill = skills.Count > 0 ? skills[skills.Count / 2] : 0,
            UrgentReorders = reorder.Where(r => r.UrgencyRank <= 2).Take(6).ToList(),
            Segments = await _rfm.SummaryAsync(rfm)
        };
        return View(vm);
    }

    public async Task<IActionResult> Forecast()
        => View(new ForecastIndexVm
        {
            Results = await _forecast.ForecastAllAsync(),
            Horizon = await _settings.GetIntAsync("ForecastHorizonDays", 30),
            HistoryDays = await _settings.GetIntAsync("ForecastHistoryDays", 120)
        });

    public async Task<IActionResult> ForecastDetail(int id)
    {
        var f = await _forecast.ForecastProductAsync(id);
        return f == null ? NotFound() : View(f);
    }

    public async Task<IActionResult> Reorder() => View(await _reorder.SuggestAsync());

    public async Task<IActionResult> Segments(string segment)
    {
        var all = await _rfm.ScoreAsync();
        return View(new SegmentsVm
        {
            Summary = await _rfm.SummaryAsync(all),
            Customers = string.IsNullOrEmpty(segment) ? all : all.Where(c => c.Segment == segment).ToList(),
            Filter = segment
        });
    }

    public async Task<IActionResult> Basket(string q) { ViewBag.Q = q; var rules = await _basket.RulesAsync(); if (!string.IsNullOrWhiteSpace(q)) rules = rules.Where(r => r.AntecedentName.Contains(q, StringComparison.OrdinalIgnoreCase) || r.ConsequentName.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList(); return View(rules); }

    public async Task<IActionResult> Abc(int days = 90)
    {
        ViewBag.Days = days;
        return View(await _abc.ClassifyAsync(Math.Clamp(days, 7, 365)));
    }

    public async Task<IActionResult> Risk(string level)
    {
        ViewBag.Level = level;
        ViewBag.Distribution = await _risk.DistributionAsync();
        return View(await _risk.FlaggedAsync(level));
    }

    [HttpPost]
    public async Task<IActionResult> RescoreAll()
    {
        var n = await _risk.RescoreAllAsync();
        await _audit.LogAsync("RescoreAll", "Order", null, $"{n} orders");
        Success($"Re-scored {n} orders with the current thresholds.");
        return RedirectToAction(nameof(Risk));
    }

    public async Task<IActionResult> Datasets()
    {
        var list = new List<DatasetInfo>();
        foreach (var d in DatasetExportService.Catalog)
            list.Add(new DatasetInfo { Key = d.Key, Title = d.Title, Description = d.Description, Columns = d.Columns, Rows = await _export.CountAsync(d.Key) });
        return View(list);
    }

    public async Task<IActionResult> Download(string id)
    {
        var csv = await _export.BuildCsvAsync(id);
        if (csv == null) return NotFound();
        await _audit.LogAsync("Export", "Dataset", id);
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", $"nexacommerce-{id}-{DateTime.Now:yyyyMMdd}.csv");
    }

    public IActionResult Methodology() => View();
}

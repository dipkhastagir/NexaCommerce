using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaCommerce.Services;
using NexaCommerce.Services.Intelligence;

namespace NexaCommerce.Controllers.Api;

/// <summary>Read-only analytics endpoints, useful for notebooks, dashboards or a mobile client.</summary>
[ApiController]
[Route("api/intelligence")]
[Authorize(Policy = "Management")]
public class IntelligenceApiController : ControllerBase
{
    private readonly ForecastingService _forecast;
    private readonly ReorderService _reorder;
    private readonly SegmentationService _rfm;
    private readonly BasketAnalysisService _basket;
    private readonly AbcAnalysisService _abc;
    private readonly ReportService _reports;

    public IntelligenceApiController(ForecastingService forecast, ReorderService reorder, SegmentationService rfm, BasketAnalysisService basket,
        AbcAnalysisService abc, ReportService reports)
    {
        _forecast = forecast; _reorder = reorder; _rfm = rfm; _basket = basket; _abc = abc; _reports = reports;
    }

    [HttpGet("stats"), Authorize(Policy = "BackOffice")]
    public async Task<IActionResult> Stats() => Ok(await _reports.StatsAsync());

    [HttpGet("forecast/{productId:int}")]
    public async Task<IActionResult> Forecast(int productId)
    {
        var f = await _forecast.ForecastProductAsync(productId);
        if (f == null) return NotFound();
        return Ok(new
        {
            f.ProductId, f.Sku, f.Name, f.Alpha, f.Beta, f.Mae, f.Rmse, f.Smape, f.NaiveMae, f.SkillRatio, f.ForecastTotal, f.DaysUntilStockout,
            forecast = f.Forecast.Select((p, i) => new { date = p.Date.ToString("yyyy-MM-dd"), units = Math.Round(p.Units, 2), lower = Math.Round(f.Lower.ElementAtOrDefault(i)?.Units ?? 0, 2), upper = Math.Round(f.Upper.ElementAtOrDefault(i)?.Units ?? 0, 2) })
        });
    }

    [HttpGet("reorder")] public async Task<IActionResult> Reorder() => Ok(await _reorder.SuggestAsync());
    [HttpGet("segments")] public async Task<IActionResult> Segments() => Ok(await _rfm.SummaryAsync());
    [HttpGet("basket-rules")] public async Task<IActionResult> Rules(int take = 50) => Ok((await _basket.RulesAsync()).Take(Math.Clamp(take, 1, 500)));
    [HttpGet("abc")] public async Task<IActionResult> Abc(int days = 90) => Ok(await _abc.ClassifyAsync(days));

    /// <summary>Public: "frequently bought together" for a product page or mobile app.</summary>
    [HttpGet("recommendations/{productId:int}"), AllowAnonymous]
    public async Task<IActionResult> Recommendations(int productId)
        => Ok((await _basket.ForProductAsync(productId, 6)).Select(r => new { productId = r.ConsequentId, name = r.ConsequentName, price = r.ConsequentPrice, confidence = Math.Round(r.Confidence, 3), lift = Math.Round(r.Lift, 2) }));
}

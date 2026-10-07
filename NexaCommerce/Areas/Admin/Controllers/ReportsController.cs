using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaCommerce.Infrastructure;
using NexaCommerce.Services;

namespace NexaCommerce.Areas.Admin.Controllers;

[Authorize(Policy = "Management")]
public class ReportsController : AdminBaseController
{
    private readonly ReportService _reports;
    public ReportsController(ReportService reports) => _reports = reports;

    public IActionResult Index() => View();

    public async Task<IActionResult> Sales(DateTime? from, DateTime? to)
    {
        var t = to ?? DateTime.Today;
        var f = from ?? t.AddDays(-29);
        if (f > t) (f, t) = (t, f);
        if ((t - f).TotalDays > 366) f = t.AddDays(-366);
        return View(await _reports.SalesAsync(f, t));
    }

    public async Task<IActionResult> Inventory() => View(await _reports.InventoryAsync());

    public async Task<IActionResult> Products() => View(await _reports.ProductPerformanceAsync());
}

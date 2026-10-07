using Microsoft.AspNetCore.Mvc;
using NexaCommerce.Infrastructure;
using NexaCommerce.Services;

namespace NexaCommerce.Areas.Admin.Controllers;

public class DashboardController : AdminBaseController
{
    private readonly ReportService _reports;
    public DashboardController(ReportService reports) => _reports = reports;

    public async Task<IActionResult> Index() => View(await _reports.DashboardAsync());
}

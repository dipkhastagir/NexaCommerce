using Microsoft.AspNetCore.Mvc;
using NexaCommerce.Data;

namespace NexaCommerce.Controllers;

/// <summary>Shown when SQL Server cannot be reached; lets the user retry without restarting.</summary>
public class SetupController : Controller
{
    private readonly DatabaseStatus _status;
    private readonly DatabaseInitializer _init;

    public SetupController(DatabaseStatus status, DatabaseInitializer init) { _status = status; _init = init; }

    public IActionResult Index() => View(_status);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry()
    {
        await _init.InitializeAsync();
        return _status.IsReady ? Redirect("/") : RedirectToAction(nameof(Index));
    }
}

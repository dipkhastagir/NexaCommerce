using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NexaCommerce.Infrastructure;

[AutoValidateAntiforgeryToken]
public abstract class BaseController : Controller
{
    protected int CurrentUserId => User.GetUserId();
    protected void Success(string message) => TempData["Success"] = message;
    protected void Error(string message) => TempData["Error"] = message;
    protected void Info(string message) => TempData["Info"] = message;
}

[Area("Admin")]
[Authorize(Policy = "BackOffice")]
public abstract class AdminBaseController : BaseController
{
}

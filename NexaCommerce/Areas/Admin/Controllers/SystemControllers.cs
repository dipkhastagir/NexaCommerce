using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaCommerce.Data;
using NexaCommerce.Infrastructure;
using NexaCommerce.Models;
using NexaCommerce.Services;

namespace NexaCommerce.Areas.Admin.Controllers;

[Authorize(Policy = "AdminOnly")]
public class UsersController : AdminBaseController
{
    private readonly UserService _users;
    private readonly AuthService _auth;
    private readonly AuditService _audit;
    public UsersController(UserService users, AuthService auth, AuditService audit) { _users = users; _auth = auth; _audit = audit; }

    public async Task<IActionResult> Index(string role, string q)
    {
        ViewBag.Role = role; ViewBag.Q = q;
        ViewBag.Roles = await _users.RolesAsync();
        return View(await _users.AllAsync(role, q));
    }

    public IActionResult Create() => View(new UserFormVm());

    [HttpPost]
    public async Task<IActionResult> Create(UserFormVm vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Password)) ModelState.AddModelError(nameof(vm.Password), "Set an initial password.");
        if (!ModelState.IsValid) return View(vm);
        var id = await _auth.RegisterAsync(vm.FullName, vm.Email, vm.Phone, vm.Password, vm.RoleName);
        if (id < 0) { ModelState.AddModelError(nameof(vm.Email), "This e-mail is already registered."); return View(vm); }
        await _audit.LogAsync("Create", "User", id, $"{vm.Email} as {vm.RoleName}");
        Success($"{vm.FullName} can now sign in as {vm.RoleName}.");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var u = await _auth.GetByIdAsync(id);
        if (u == null) return NotFound();
        return View(new UserFormVm { UserId = u.UserId, FullName = u.FullName, Email = u.Email, Phone = u.Phone, RoleName = u.RoleName, IsActive = u.IsActive });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, UserFormVm vm)
    {
        vm.UserId = id;
        ModelState.Remove(nameof(vm.Password));
        var current = await _auth.GetByIdAsync(id);
        if (current == null) return NotFound();
        if (id == CurrentUserId && (vm.RoleName != "Admin" || !vm.IsActive))
            ModelState.AddModelError(string.Empty, "You can't remove your own administrator access.");
        if (current.RoleName == "Admin" && vm.RoleName != "Admin" && await _users.AdminCountAsync() <= 1)
            ModelState.AddModelError(string.Empty, "At least one active administrator is required.");
        if (!ModelState.IsValid) { vm.Email = current.Email; return View(vm); }
        await _users.UpdateAsync(vm);
        if (!string.IsNullOrWhiteSpace(vm.Password))
        {
            if (vm.Password.Length < 8) { ModelState.AddModelError(nameof(vm.Password), "Use at least 8 characters."); vm.Email = current.Email; return View(vm); }
            await _auth.SetPasswordAsync(id, vm.Password);
        }
        await _audit.LogAsync("Update", "User", id, $"role {vm.RoleName}, active {vm.IsActive}");
        Success("User saved.");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Roles() => View(await _users.RolesAsync());
}

[Authorize(Policy = "AdminOnly")]
public class AuditLogsController : AdminBaseController
{
    private readonly AuditService _audit;
    public AuditLogsController(AuditService audit) => _audit = audit;

    public async Task<IActionResult> Index(string q, string entity, int page = 1)
    {
        ViewBag.Q = q; ViewBag.Entity = entity;
        ViewBag.Entities = await _audit.EntitiesAsync();
        return View(await _audit.SearchAsync(q, entity, page));
    }
}

[Authorize(Policy = "AdminOnly")]
public class SettingsController : AdminBaseController
{
    private readonly SettingsService _settings;
    private readonly AuditService _audit;
    private readonly DatabaseStatus _status;
    public SettingsController(SettingsService settings, AuditService audit, DatabaseStatus status) { _settings = settings; _audit = audit; _status = status; }

    public async Task<IActionResult> Index()
    {
        ViewBag.Db = _status;
        return View(new SettingsVm { Settings = await _settings.ListAsync() });
    }

    [HttpPost]
    public async Task<IActionResult> Index(Dictionary<string, string> values)
    {
        await _settings.SaveAsync(values ?? new());
        await _audit.LogAsync("Update", "Settings", null, string.Join(", ", (values ?? new()).Select(kv => $"{kv.Key}={kv.Value}")));
        Success("Settings saved.");
        return RedirectToAction(nameof(Index));
    }
}

public class NotificationsController : AdminBaseController
{
    private readonly NotificationService _notify;
    public NotificationsController(NotificationService notify) => _notify = notify;

    public async Task<IActionResult> Index(bool unread = false)
    {
        ViewBag.Unread = unread;
        return View(await _notify.ForStaffAsync(CurrentUserId, unread));
    }

    public async Task<IActionResult> Open(int id, string link)
    {
        await _notify.MarkReadAsync(id);
        return !string.IsNullOrEmpty(link) && Url.IsLocalUrl(link) ? Redirect(link) : RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> MarkAll()
    {
        await _notify.MarkAllReadAsync(CurrentUserId);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ClearRead()
    {
        await _notify.DeleteReadAsync(CurrentUserId);
        return RedirectToAction(nameof(Index));
    }
}

public class MessagesController : AdminBaseController
{
    private readonly ContactService _contact;
    public MessagesController(ContactService contact) => _contact = contact;

    public async Task<IActionResult> Index() => View(await _contact.AllAsync());

    [HttpPost]
    public async Task<IActionResult> Handle(int id)
    {
        await _contact.MarkHandledAsync(id);
        return RedirectToAction(nameof(Index));
    }
}

public class ApiDocsController : AdminBaseController
{
    public IActionResult Index() => View();
}

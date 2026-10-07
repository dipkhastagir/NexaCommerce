using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexaCommerce.Infrastructure;
using NexaCommerce.Models;
using NexaCommerce.Services;

namespace NexaCommerce.Controllers;

public class AccountController : BaseController
{
    private readonly AuthService _auth;
    private readonly AddressService _addresses;
    private readonly AuditService _audit;

    public AccountController(AuthService auth, AddressService addresses, AuditService audit)
    {
        _auth = auth; _addresses = addresses; _audit = audit;
    }

    [HttpGet]
    public IActionResult Login(string returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectAfterLogin(returnUrl);
        return View(new LoginVm { ReturnUrl = returnUrl });
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var user = await _auth.ValidateAsync(vm.Email, vm.Password);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "That e-mail and password don't match an active account.");
            await _audit.LogAsync("LoginFailed", "User", null, vm.Email);
            return View(vm);
        }
        await _auth.SignInAsync(HttpContext, user, vm.RememberMe);
        await _audit.LogAsync("Login", "User", user.UserId, user.Email);
        Success($"Welcome back, {user.FullName.Split(' ')[0]}.");
        if (string.IsNullOrEmpty(vm.ReturnUrl) && user.RoleName != "Customer") return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
        return RedirectAfterLogin(vm.ReturnUrl);
    }

    [HttpGet]
    public IActionResult Register() => View(new RegisterVm());

    [HttpPost]
    public async Task<IActionResult> Register(RegisterVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var id = await _auth.RegisterAsync(vm.FullName, vm.Email, vm.Phone, vm.Password);
        if (id < 0)
        {
            ModelState.AddModelError(nameof(vm.Email), "An account with this e-mail already exists.");
            return View(vm);
        }
        var user = await _auth.GetByIdAsync(id);
        await _auth.SignInAsync(HttpContext, user, false);
        await _audit.LogAsync("Register", "User", id, vm.Email);
        Success("Your account is ready. Use code WELCOME10 for 10% off your first order.");
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await _audit.LogAsync("Logout", "User", CurrentUserId);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordVm());

    [HttpPost]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var token = await _auth.CreateResetTokenAsync(vm.Email);
        // No mail server in development: the link is shown on the next page as a simulated e-mail.
        TempData["ResetLink"] = token == null ? null : Url.Action(nameof(ResetPassword), "Account", new { token }, Request.Scheme);
        TempData["ResetEmail"] = vm.Email;
        if (token != null) await _audit.LogAsync("ResetRequested", "User", null, vm.Email);
        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    public IActionResult ForgotPasswordConfirmation()
    {
        ViewBag.ResetLink = TempData["ResetLink"] as string;
        ViewBag.Email = TempData["ResetEmail"] as string;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string token)
    {
        if (string.IsNullOrEmpty(token) || !await _auth.IsResetTokenValidAsync(token))
        {
            Error("This reset link has expired or was already used. Request a new one.");
            return RedirectToAction(nameof(ForgotPassword));
        }
        return View(new ResetPasswordVm { Token = token });
    }

    [HttpPost]
    public async Task<IActionResult> ResetPassword(ResetPasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var userId = await _auth.ResetPasswordAsync(vm.Token, vm.NewPassword);
        if (userId == 0)
        {
            Error("This reset link has expired or was already used. Request a new one.");
            return RedirectToAction(nameof(ForgotPassword));
        }
        await _audit.LogAsync("PasswordReset", "User", userId);
        Success("Password updated. Sign in with your new password.");
        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var vm = await _auth.GetProfileAsync(CurrentUserId);
        return View(vm);
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> Profile(ProfileVm vm)
    {
        if (!ModelState.IsValid)
        {
            var current = await _auth.GetProfileAsync(CurrentUserId);
            vm.Email = current.Email; vm.RoleName = current.RoleName; vm.CreatedAt = current.CreatedAt;
            vm.OrderCount = current.OrderCount; vm.TotalSpent = current.TotalSpent;
            return View(vm);
        }
        await _auth.UpdateProfileAsync(CurrentUserId, vm.FullName, vm.Phone);
        var user = await _auth.GetByIdAsync(CurrentUserId);
        await _auth.SignInAsync(HttpContext, user, false);
        Success("Profile saved.");
        return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordVm());

    [Authorize, HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordVm vm)
    {
        if (!ModelState.IsValid) return View(vm);
        if (!await _auth.ChangePasswordAsync(CurrentUserId, vm.CurrentPassword, vm.NewPassword))
        {
            ModelState.AddModelError(nameof(vm.CurrentPassword), "Current password is incorrect.");
            return View(vm);
        }
        await _audit.LogAsync("PasswordChanged", "User", CurrentUserId);
        Success("Password changed.");
        return RedirectToAction(nameof(Profile));
    }

    [Authorize]
    public async Task<IActionResult> Addresses() => View(await _addresses.ForUserAsync(CurrentUserId));

    [Authorize, HttpGet]
    public async Task<IActionResult> AddressForm(int? id)
    {
        if (id == null) return View(new Address());
        var a = await _addresses.GetAsync(id.Value, CurrentUserId);
        return a == null ? NotFound() : View(a);
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> AddressForm(Address model)
    {
        if (!ModelState.IsValid) return View(model);
        model.UserId = CurrentUserId;
        await _addresses.SaveAsync(model);
        Success("Address saved.");
        return RedirectToAction(nameof(Addresses));
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> DeleteAddress(int id)
    {
        await _addresses.DeleteAsync(id, CurrentUserId);
        Info("Address removed.");
        return RedirectToAction(nameof(Addresses));
    }

    public IActionResult AccessDenied() => View();

    private IActionResult RedirectAfterLogin(string returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction("Index", "Home");
}

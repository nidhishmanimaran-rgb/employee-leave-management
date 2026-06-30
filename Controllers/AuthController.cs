using EmployeeLeaveManagementSystem.Models;
using EmployeeLeaveManagementSystem.Services;
using Microsoft.AspNetCore.Mvc;

namespace EmployeeLeaveManagementSystem.Controllers;

public class AuthController : Controller
{
    private readonly AppUserStore _userStore;
    private readonly EmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AppUserStore userStore,
        EmailService emailService,
        IConfiguration config,
        ILogger<AuthController> logger)
    {
        _userStore = userStore;
        _emailService = emailService;
        _config = config;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login()
    {
        if (AuthSession.IsSignedIn(HttpContext.Session))
            return RedirectToRoleDashboard(AuthSession.GetRole(HttpContext.Session));

        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = _userStore.ValidateUser(model.Email, model.Password);
        if (user is null)
        {
            var existingUser = _userStore.GetByEmail(model.Email);
            TempData["ErrorMessage"] = existingUser?.Status switch
            {
                "Pending" => "Your account is waiting for HR approval.",
                "Rejected" => "Your registration was rejected. Please contact HR.",
                _ => "Invalid company email or password."
            };
            return View(model);
        }

        AuthSession.SignIn(HttpContext.Session, user);
        return RedirectToRoleDashboard(user.Role);
    }

    [HttpGet]
    public IActionResult Register()
    {
        if (AuthSession.IsSignedIn(HttpContext.Session))
            return RedirectToRoleDashboard(AuthSession.GetRole(HttpContext.Session));

        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = _userStore.RegisterPending(model);
        if (!result.Succeeded || result.User is null)
        {
            TempData["ErrorMessage"] = result.Message;
            return View(model);
        }

        var emailWarning = await TrySendRegistrationEmailAsync(result.User, cancellationToken);
        TempData["SuccessMessage"] = emailWarning is null
            ? "Registration submitted. HR will approve your account before login."
            : $"Registration submitted. {emailWarning}";

        return RedirectToAction(nameof(Login));
    }

    [HttpPost("/register")]
    public async Task<IActionResult> RegisterApi(
        [FromBody] RegisterViewModel model,
        CancellationToken cancellationToken)
    {
        if (!TryValidateModel(model))
            return BadRequest(ModelState);

        var result = _userStore.RegisterPending(model);
        if (!result.Succeeded || result.User is null)
            return Conflict(new { success = false, message = result.Message });

        var emailWarning = await TrySendRegistrationEmailAsync(result.User, cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Registration submitted for HR approval.",
            userId = result.User.UserId,
            employeeId = result.User.EmployeeId,
            status = result.User.Status,
            emailWarning
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        AuthSession.SignOut(HttpContext.Session);
        return RedirectToAction(nameof(Login));
    }

    private async Task<string?> TrySendRegistrationEmailAsync(AppUser user, CancellationToken cancellationToken)
    {
        if (_config.GetValue<bool>("Smtp:DisableSending"))
            return "Email sending is disabled in this environment.";

        try
        {
            var hrEmail = _config["HrEmail"] ?? "hr@company.com";
            var approvalUrl = Url.Action("Index", "HrDashboard", values: null, protocol: Request.Scheme);

            await _emailService.SendNewEmployeeRegistrationEmailAsync(
                hrEmail,
                user.Name,
                user.Email,
                user.Department,
                user.JobTitle,
                approvalUrl,
                cancellationToken);

            return null;
        }
        catch (Exception ex) when (_config.GetValue<bool>("Smtp:ContinueWhenEmailFails"))
        {
            _logger.LogWarning(ex, "Registration was saved, but the HR notification email could not be sent.");
            return "HR email could not be sent. Check SMTP settings, but the pending registration is saved.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Registration was saved, but the HR notification email could not be sent.");
            return "HR email could not be sent. Check SMTP settings, but the pending registration is saved.";
        }
    }

    private IActionResult RedirectToRoleDashboard(string? role)
    {
        if (string.Equals(role, "Employee", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction("Index", "EmployeeDashboard");

        if (string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction("Index", "ManagerDashboard");

        if (AuthSession.IsHrRole(role))
            return RedirectToAction("Index", "HrDashboard");

        AuthSession.SignOut(HttpContext.Session);
        return RedirectToAction(nameof(Login));
    }
}

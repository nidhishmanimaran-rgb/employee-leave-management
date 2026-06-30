using EmployeeLeaveManagementSystem.Models;
using EmployeeLeaveManagementSystem.Services;
using Microsoft.AspNetCore.Mvc;

namespace EmployeeLeaveManagementSystem.Controllers;

public class HomeController : Controller
{
    // GET: /
    public IActionResult Index()
    {
        if (!AuthSession.IsRole(HttpContext.Session, "Employee"))
            return RedirectToRoleDashboard(AuthSession.GetRole(HttpContext.Session));

        var user = HttpContext.RequestServices
            .GetRequiredService<AppUserStore>()
            .GetByEmail(AuthSession.GetEmail(HttpContext.Session));

        if (user is null)
            return RedirectToAction("Login", "Auth");

        return View(CreateLeaveRequestFromUser(user));
    }

    // POST: / (submit MVC form)
    // This action does the required flow:
    // MVC Form -> Web API logic -> SQL Server insert + email -> Success message.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(LeaveRequest request)
    {
        if (!AuthSession.IsRole(HttpContext.Session, "Employee"))
            return RedirectToRoleDashboard(AuthSession.GetRole(HttpContext.Session));

        var user = HttpContext.RequestServices
            .GetRequiredService<AppUserStore>()
            .GetByEmail(AuthSession.GetEmail(HttpContext.Session));

        if (user is null)
            return RedirectToAction("Login", "Auth");

        ApplyUserDetails(request, user);
        ModelState.Clear();
        TryValidateModel(request);

        if (!ModelState.IsValid)
            return View(request);

        // Call Web API logic directly using DI-provided dependencies.
        // 1) Save to SQL + 2) send HR email happen inside the API controller.
        var emailService = HttpContext.RequestServices.GetRequiredService<EmailService>();
        var sqlLeaveRequestStore = HttpContext.RequestServices.GetRequiredService<SqlLeaveRequestStore>();
        var publicHolidayService = HttpContext.RequestServices.GetRequiredService<PublicHolidayService>();
        var apiController = new LeaveApiController(
            emailService,
            HttpContext.RequestServices.GetRequiredService<IConfiguration>(),
            sqlLeaveRequestStore,
            HttpContext.RequestServices.GetRequiredService<AppUserStore>(),
            publicHolidayService,
            HttpContext.RequestServices.GetRequiredService<ILogger<LeaveApiController>>());
        var result = await apiController.PostLeaveRequest(request, CancellationToken.None);

        if (result is OkObjectResult ok)
        {
            TempData["SuccessMessage"] = GetResultMessage(ok.Value, "Leave request submitted successfully.");
            return RedirectToAction("Index", "EmployeeDashboard");
        }

        if (result is ObjectResult objectResult)
        {
            TempData["ErrorMessage"] = GetResultMessage(objectResult.Value, "Something went wrong submitting the leave request.");
            return RedirectToAction(nameof(Index));
        }

        TempData["ErrorMessage"] = "Something went wrong submitting the leave request.";
        return RedirectToAction(nameof(Index));
    }

    private IActionResult RedirectToRoleDashboard(string? role)
    {
        if (string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction("Index", "ManagerDashboard");

        if (AuthSession.IsHrRole(role))
            return RedirectToAction("Index", "HrDashboard");

        return RedirectToAction("Login", "Auth");
    }

    private static LeaveRequest CreateLeaveRequestFromUser(AppUser user)
    {
        return new LeaveRequest
        {
            EmployeeId = user.EmployeeId,
            EmployeeName = user.Name,
            EmployeeEmail = user.Email,
            EmployeeRole = user.JobTitle,
            EmployeeDepartment = user.Department,
            EmployeePhone = user.Phone
        };
    }

    private static void ApplyUserDetails(LeaveRequest request, AppUser user)
    {
        request.EmployeeId = user.EmployeeId;
        request.EmployeeName = user.Name;
        request.EmployeeEmail = user.Email;
        request.EmployeeRole = user.JobTitle;
        request.EmployeeDepartment = user.Department;
        request.EmployeePhone = user.Phone;
    }

    private static string GetResultMessage(object? value, string fallback)
    {
        if (value is null)
            return fallback;

        if (value is string message)
            return string.IsNullOrWhiteSpace(message) ? fallback : message;

        var messageProperty = value.GetType().GetProperty("message") ?? value.GetType().GetProperty("Message");
        var resultMessage = messageProperty?.GetValue(value)?.ToString();

        return string.IsNullOrWhiteSpace(resultMessage) ? fallback : resultMessage;
    }
}

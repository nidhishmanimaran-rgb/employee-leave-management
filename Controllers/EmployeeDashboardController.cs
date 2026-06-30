using EmployeeLeaveManagementSystem.Models;
using EmployeeLeaveManagementSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EmployeeLeaveManagementSystem.Controllers;

public class EmployeeDashboardController : Controller
{
    private readonly AppUserStore _userStore;
    private readonly SqlLeaveRequestStore _leaveRequestStore;
    private readonly ILogger<EmployeeDashboardController> _logger;

    public EmployeeDashboardController(
        AppUserStore userStore,
        SqlLeaveRequestStore leaveRequestStore,
        ILogger<EmployeeDashboardController> logger)
    {
        _userStore = userStore;
        _leaveRequestStore = leaveRequestStore;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!AuthSession.IsRole(HttpContext.Session, "Employee"))
            return RedirectToAction("Login", "Auth");

        var user = _userStore.GetByEmail(AuthSession.GetEmail(HttpContext.Session));
        if (user is null)
            return RedirectToAction("Logout", "Auth");

        IReadOnlyList<LeaveRequestRecord> requests;
        try
        {
            requests = await _leaveRequestStore.GetRequestsAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database connection failed while loading employee dashboard.");
            TempData["ErrorMessage"] = "Database connection failed. Leave history is temporarily unavailable.";
            requests = Array.Empty<LeaveRequestRecord>();
        }

        var employeeRequests = requests
            .Where(request => BelongsToUser(request, user))
            .OrderByDescending(request => request.SubmittedAtUtc)
            .ToList();

        var approvedDays = employeeRequests
            .Where(request => request.Status.Equals("Accepted", StringComparison.OrdinalIgnoreCase))
            .Sum(GetInclusiveLeaveDays);

        return View(new EmployeeDashboardViewModel
        {
            User = user,
            Requests = employeeRequests,
            LeaveBalanceDays = user.LeaveBalanceDays,
            ApprovedLeaveDays = approvedDays,
            PendingRequests = employeeRequests.Count(request =>
                request.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase)),
            RejectedRequests = employeeRequests.Count(request =>
                request.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
        });
    }

    private static bool BelongsToUser(LeaveRequestRecord request, AppUser user)
    {
        return request.EmployeeEmail.Equals(user.Email, StringComparison.OrdinalIgnoreCase)
            || request.EmployeeId.Equals(user.EmployeeId, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetInclusiveLeaveDays(LeaveRequestRecord request)
    {
        if (request.FromDate == default || request.ToDate == default || request.ToDate < request.FromDate)
            return 0;

        return request.ToDate.DayNumber - request.FromDate.DayNumber + 1;
    }
}

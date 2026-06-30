using EmployeeLeaveManagementSystem.Models;
using EmployeeLeaveManagementSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EmployeeLeaveManagementSystem.Controllers;

public class HrDashboardController : Controller
{
    private const string AuthenticatedSessionKey = "HrDashboardAuthenticated";

    private static readonly HashSet<string> AllowedDecisions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accepted",
        "Rejected"
    };

    private readonly SqlLeaveRequestStore _leaveRequestStore;
    private readonly LeaveRequestStore _legacyLeaveRequestStore;

    private readonly EmailService _emailService;
    private readonly AppUserStore _userStore;

    private readonly IConfiguration _config;
    private readonly ILogger<HrDashboardController> _logger;


    public HrDashboardController(
        SqlLeaveRequestStore leaveRequestStore,
        LeaveRequestStore legacyLeaveRequestStore,
        EmailService emailService,
        AppUserStore userStore,
        IConfiguration config,
        ILogger<HrDashboardController> logger)
    {
        _leaveRequestStore = leaveRequestStore;
        _legacyLeaveRequestStore = legacyLeaveRequestStore;
        _emailService = emailService;
        _userStore = userStore;
        _config = config;
        _logger = logger;
    }


    public async Task<IActionResult> Index(string? employeeId, CancellationToken cancellationToken)
    {
        if (!IsAuthenticated())
            return RedirectToAction(nameof(Login));

        var useSqlForDashboards = _config.GetValue<bool>("LeaveRequests:UseSqlServerForDashboards", false);

        IReadOnlyList<LeaveRequestRecord> allRequests;
        if (useSqlForDashboards)
        {
            try
            {
                allRequests = await _leaveRequestStore.GetRequestsAsync(cancellationToken);
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Database connection failed while loading HR dashboard.");
                TempData["ErrorMessage"] = "Database connection failed. Leave requests are temporarily unavailable.";
                allRequests = Array.Empty<LeaveRequestRecord>();
            }
        }
        else
        {
            allRequests = await _legacyLeaveRequestStore.GetRequestsAsync(cancellationToken);
        }


        var filteredRequests = FilterRequestsByEmployeeId(allRequests, employeeId);
        var employeeSummaries = BuildEmployeeSummaries(filteredRequests);

        var activeUsers = _userStore.GetActiveUsers();
        var pendingRegistrations = _userStore.GetPendingRegistrations();
        var currentUserRole = AuthSession.GetRole(HttpContext.Session) ?? "HR";

        return View(new HrDashboardViewModel
        {
            CurrentUserRole = currentUserRole,
            AssignableRoles = AppUserStore.GetAssignableRoles(currentUserRole),
            PendingRegistrations = pendingRegistrations,
            ActiveUsers = activeUsers,
            EmployeeCount = activeUsers.Count(user =>
                user.Role.Equals("Employee", StringComparison.OrdinalIgnoreCase)),
            ManagerCount = activeUsers.Count(user =>
                user.Role.Equals("Manager", StringComparison.OrdinalIgnoreCase)),
            SearchEmployeeId = employeeId?.Trim(),
            SelectedUser = GetSelectedUser(activeUsers, employeeId),
            SelectedEmployee = GetSelectedEmployee(employeeSummaries, employeeId),
            EmployeeSummaries = employeeSummaries,
            Requests = filteredRequests
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveRegistration(string userId, CancellationToken cancellationToken)
    {
        if (!IsAuthenticated())
            return RedirectToAction(nameof(Login));

        var result = _userStore.ApproveRegistration(userId);
        if (!result.Succeeded || result.User is null)
        {
            TempData["ErrorMessage"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        var emailWarning = await TrySendApprovalEmailAsync(result.User, cancellationToken);
        TempData["SuccessMessage"] = emailWarning is null
            ? $"Account approved for {result.User.Name}."
            : $"Account approved for {result.User.Name}. {emailWarning}";

        return RedirectToAction(nameof(Index));
    }

    [HttpPut("/approve/{id}")]
    public async Task<IActionResult> ApproveRegistrationApi(string id, CancellationToken cancellationToken)
    {
        if (!IsAuthenticated())
            return Unauthorized(new { success = false, message = "HR or Admin login is required." });

        var result = _userStore.ApproveRegistration(id);
        if (!result.Succeeded || result.User is null)
            return BadRequest(new { success = false, message = result.Message });

        var emailWarning = await TrySendApprovalEmailAsync(result.User, cancellationToken);

        return Ok(new
        {
            success = true,
            message = result.Message,
            userId = result.User.UserId,
            employeeId = result.User.EmployeeId,
            status = result.User.Status,
            role = result.User.Role,
            emailWarning
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectRegistration(string userId, CancellationToken cancellationToken)
    {
        if (!IsAuthenticated())
            return RedirectToAction(nameof(Login));

        var result = _userStore.RejectRegistration(userId);
        if (!result.Succeeded || result.User is null)
        {
            TempData["ErrorMessage"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        var emailWarning = await TrySendRejectionEmailAsync(result.User, cancellationToken);
        TempData["SuccessMessage"] = emailWarning is null
            ? $"Registration rejected for {result.User.Name}."
            : $"Registration rejected for {result.User.Name}. {emailWarning}";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateRole(string userId, string role)
    {
        if (!IsAuthenticated())
            return RedirectToAction(nameof(Login));

        var result = _userStore.UpdateRole(userId, role, AuthSession.GetRole(HttpContext.Session));
        if (!result.Succeeded)
            TempData["ErrorMessage"] = result.Message;
        else
            TempData["SuccessMessage"] = $"{result.User?.Name} is now {result.User?.Role}.";

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Login()
    {
        return RedirectToAction("Login", "Auth");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Login(string username, string password)
    {
        var configuredUsername = _config["HrDashboard:Username"] ?? "hr123";
        var configuredPassword = _config["HrDashboard:Password"] ?? "12345678";

        if (string.Equals(username, configuredUsername, StringComparison.Ordinal)
            && string.Equals(password, configuredPassword, StringComparison.Ordinal))
        {
            HttpContext.Session.SetString(AuthenticatedSessionKey, "true");
            HttpContext.Session.SetString(AuthSession.UserEmailKey, _config["HrEmail"] ?? "hr@company.com");
            HttpContext.Session.SetString(AuthSession.UserRoleKey, "HR");
            HttpContext.Session.SetString(AuthSession.UserNameKey, "HR");
            return RedirectToAction(nameof(Index));
        }

        TempData["ErrorMessage"] = "Invalid HR username or password.";
        return RedirectToAction(nameof(Login));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        AuthSession.SignOut(HttpContext.Session);
        return RedirectToAction("Login", "Auth");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(
        string requestId,
        string decision,
        string? comment,
        CancellationToken cancellationToken)
    {
        if (!IsAuthenticated())
            return RedirectToAction(nameof(Login));

        if (string.IsNullOrWhiteSpace(requestId) || !AllowedDecisions.Contains(decision))
        {
            TempData["ErrorMessage"] = "Please choose a valid leave request decision.";
            return RedirectToAction(nameof(Index));
        }

        LeaveRequestRecord? request;
        try
        {
            request = await _leaveRequestStore.GetRequestAsync(requestId, cancellationToken);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database connection failed while reading leave request {RequestId}.", requestId);
            TempData["ErrorMessage"] = "Database connection failed. Please try again after SQL Server is available.";
            return RedirectToAction(nameof(Index));
        }

        if (request is null)
        {
            TempData["ErrorMessage"] = "Leave request was not found.";
            return RedirectToAction(nameof(Index));
        }

        if (!request.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = "This leave request has already been decided.";
            return RedirectToAction(nameof(Index));
        }

        var normalizedDecision = decision.Equals("Accepted", StringComparison.OrdinalIgnoreCase)
            ? "Accepted"
            : "Rejected";

        try
        {
            await _leaveRequestStore.SaveDecisionAsync(request.RequestId, normalizedDecision, comment, cancellationToken);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database connection failed while saving HR leave decision.");
            TempData["ErrorMessage"] = "Database connection failed. The leave decision was not saved.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await _emailService.SendLeaveDecisionEmailAsync(
                request.EmployeeEmail,
                request.EmployeeId,
                request.EmployeeRole,
                request.EmployeeDepartment,
                request.EmployeeName,
                decision,
                request.FromDate,
                request.ToDate,
                request.Reason,
                comment,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Leave decision was saved, but notification email failed. RequestId={RequestId}",
                request.RequestId);
            TempData["ErrorMessage"] = "Leave decision saved, but the notification email could not be sent.";
            return RedirectToAction(nameof(Index));
        }

        TempData["SuccessMessage"] =
            $"Leave request {normalizedDecision.ToLowerInvariant()} and email sent to {request.EmployeeEmail}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetData(CancellationToken cancellationToken)
    {
        if (!IsAuthenticated())
            return RedirectToAction(nameof(Login));

        try
        {
            await _leaveRequestStore.ResetAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database connection failed while resetting leave requests.");
            TempData["ErrorMessage"] = "Database connection failed. Leave request data was not reset.";
            return RedirectToAction(nameof(Index));
        }

        TempData["SuccessMessage"] = "Leave request database has been reset.";
        return RedirectToAction(nameof(Index));
    }

    private bool IsAuthenticated()
    {
        return AuthSession.CanAccessHr(HttpContext.Session);
    }

    private async Task<string?> TrySendApprovalEmailAsync(AppUser user, CancellationToken cancellationToken)
    {
        if (_config.GetValue<bool>("Smtp:DisableSending"))
            return "Email sending is disabled in this environment.";

        try
        {
            await _emailService.SendAccountApprovedEmailAsync(
                user.Email,
                user.Name,
                cancellationToken);

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Account approval email could not be sent.");
            return "Approval email could not be sent. Check SMTP settings.";
        }
    }

    private async Task<string?> TrySendRejectionEmailAsync(AppUser user, CancellationToken cancellationToken)
    {
        if (_config.GetValue<bool>("Smtp:DisableSending"))
            return "Email sending is disabled in this environment.";

        try
        {
            await _emailService.SendRegistrationRejectedEmailAsync(
                user.Email,
                user.Name,
                cancellationToken);

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Registration rejection email could not be sent.");
            return "Rejection email could not be sent. Check SMTP settings.";
        }
    }

    private static IReadOnlyList<EmployeeLeaveSummary> BuildEmployeeSummaries(
        IReadOnlyList<LeaveRequestRecord> requests)
    {
        return requests
            .GroupBy(GetEmployeeSummaryKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var orderedRequests = group
                    .OrderByDescending(request => request.SubmittedAtUtc)
                    .ToList();
                var latestRequest = orderedRequests[0];
                var acceptedRequests = orderedRequests
                    .Where(request => request.Status.Equals("Accepted", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                return new EmployeeLeaveSummary
                {
                    EmployeeId = string.IsNullOrWhiteSpace(latestRequest.EmployeeId)
                        ? "Not recorded"
                        : latestRequest.EmployeeId,
                    EmployeeName = latestRequest.EmployeeName,
                    EmployeeEmail = latestRequest.EmployeeEmail,
                    EmployeeRole = GetDisplayValue(latestRequest.EmployeeRole),
                    EmployeeDepartment = GetDisplayValue(latestRequest.EmployeeDepartment),
                    EmployeePhone = GetDisplayValue(latestRequest.EmployeePhone),
                    TotalRequests = orderedRequests.Count,
                    AcceptedRequests = acceptedRequests.Count,
                    RejectedRequests = orderedRequests.Count(request =>
                        request.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)),
                    PendingRequests = orderedRequests.Count(request =>
                        request.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase)),
                    ApprovedLeaveDays = acceptedRequests.Sum(GetInclusiveLeaveDays),
                    RequestedLeaveDays = orderedRequests.Sum(GetInclusiveLeaveDays),
                    LatestStatus = latestRequest.Status,
                    FirstSubmittedAtUtc = orderedRequests[^1].SubmittedAtUtc,
                    LastSubmittedAtUtc = latestRequest.SubmittedAtUtc
                };
            })
            .OrderBy(summary => summary.EmployeeName)
            .ThenBy(summary => summary.EmployeeId)
            .ToList();
    }

    private static string GetEmployeeSummaryKey(LeaveRequestRecord request)
    {
        if (!string.IsNullOrWhiteSpace(request.EmployeeId))
            return request.EmployeeId.Trim();

        if (!string.IsNullOrWhiteSpace(request.EmployeeEmail))
            return request.EmployeeEmail.Trim();

        return request.EmployeeName.Trim();
    }

    private static int GetInclusiveLeaveDays(LeaveRequestRecord request)
    {
        if (request.FromDate == default || request.ToDate == default || request.ToDate < request.FromDate)
            return 0;

        return request.ToDate.DayNumber - request.FromDate.DayNumber + 1;
    }

    private static IReadOnlyList<LeaveRequestRecord> FilterRequestsByEmployeeId(
        IReadOnlyList<LeaveRequestRecord> requests,
        string? employeeId)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return requests;

        var normalizedEmployeeId = employeeId.Trim();

        return requests
            .Where(request => !string.IsNullOrWhiteSpace(request.EmployeeId)
                && request.EmployeeId.Contains(normalizedEmployeeId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static EmployeeLeaveSummary? GetSelectedEmployee(
        IReadOnlyList<EmployeeLeaveSummary> summaries,
        string? employeeId)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return null;

        var normalizedEmployeeId = employeeId.Trim();

        return summaries.FirstOrDefault(summary =>
                summary.EmployeeId.Equals(normalizedEmployeeId, StringComparison.OrdinalIgnoreCase))
            ?? (summaries.Count == 1 ? summaries[0] : null);
    }

    private static AppUser? GetSelectedUser(IReadOnlyList<AppUser> users, string? employeeId)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return null;

        var normalizedEmployeeId = employeeId.Trim();

        return users.FirstOrDefault(user =>
                user.EmployeeId.Equals(normalizedEmployeeId, StringComparison.OrdinalIgnoreCase))
            ?? users.FirstOrDefault(user =>
                user.EmployeeId.Contains(normalizedEmployeeId, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetDisplayValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Not recorded" : value.Trim();
    }
}

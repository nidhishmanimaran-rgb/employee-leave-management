using EmployeeLeaveManagementSystem.Models;
using EmployeeLeaveManagementSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EmployeeLeaveManagementSystem.Controllers;

public class ManagerDashboardController : Controller
{
    private static readonly HashSet<string> AllowedDecisions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accepted",
        "Rejected"
    };

    private readonly AppUserStore _userStore;
    private readonly SqlLeaveRequestStore _leaveRequestStore;
    private readonly EmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<ManagerDashboardController> _logger;

    public ManagerDashboardController(
        AppUserStore userStore,
        SqlLeaveRequestStore leaveRequestStore,
        EmailService emailService,
        IConfiguration config,
        ILogger<ManagerDashboardController> logger)
    {
        _userStore = userStore;
        _leaveRequestStore = leaveRequestStore;
        _emailService = emailService;
        _config = config;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!AuthSession.IsRole(HttpContext.Session, "Manager"))
            return RedirectToAction("Login", "Auth");

        var user = _userStore.GetByEmail(AuthSession.GetEmail(HttpContext.Session));
        if (user is null)
            return RedirectToAction("Logout", "Auth");

        var teamMembers = _userStore.GetTeamMembers(user.Email);
        IReadOnlyList<LeaveRequestRecord> teamRequests;
        try
        {
            teamRequests = await GetTeamRequestsAsync(teamMembers, cancellationToken);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database connection failed while loading manager dashboard.");
            TempData["ErrorMessage"] = "Database connection failed. Team leave requests are temporarily unavailable.";
            teamRequests = Array.Empty<LeaveRequestRecord>();
        }

        return View(new ManagerDashboardViewModel
        {
            User = user,
            TeamMembers = teamMembers,
            TeamRequests = teamRequests,
            PendingRequests = teamRequests.Count(request =>
                request.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase)),
            AcceptedRequests = teamRequests.Count(request =>
                request.Status.Equals("Accepted", StringComparison.OrdinalIgnoreCase)),
            RejectedRequests = teamRequests.Count(request =>
                request.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(
        string requestId,
        string decision,
        string? comment,
        CancellationToken cancellationToken)
    {
        if (!AuthSession.IsRole(HttpContext.Session, "Manager"))
            return RedirectToAction("Login", "Auth");

        if (string.IsNullOrWhiteSpace(requestId) || !AllowedDecisions.Contains(decision))
        {
            TempData["ErrorMessage"] = "Please choose a valid team leave request decision.";
            return RedirectToAction(nameof(Index));
        }

        var user = _userStore.GetByEmail(AuthSession.GetEmail(HttpContext.Session));
        if (user is null)
            return RedirectToAction("Logout", "Auth");

        var teamMembers = _userStore.GetTeamMembers(user.Email);
        LeaveRequestRecord? request;
        try
        {
            request = (await GetTeamRequestsAsync(teamMembers, cancellationToken))
                .FirstOrDefault(teamRequest => teamRequest.RequestId == requestId);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database connection failed while deciding team leave request.");
            TempData["ErrorMessage"] = "Database connection failed. Please try again after SQL Server is available.";
            return RedirectToAction(nameof(Index));
        }

        if (request is null)
        {
            TempData["ErrorMessage"] = "Leave request was not found in your team.";
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
            _logger.LogError(ex, "Database connection failed while saving manager leave decision.");
            TempData["ErrorMessage"] = "Database connection failed. The leave decision was not saved.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            if (!_config.GetValue<bool>("Smtp:DisableSending"))
            {
                await _emailService.SendLeaveDecisionEmailAsync(
                    request.EmployeeEmail,
                    request.EmployeeId,
                    request.EmployeeRole,
                    request.EmployeeDepartment,
                    request.EmployeeName,
                    normalizedDecision,
                    request.FromDate,
                    request.ToDate,
                    request.Reason,
                    comment,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Leave decision was saved, but manager notification email failed. RequestId={RequestId}", request.RequestId);
            TempData["ErrorMessage"] = "Leave decision saved, but the notification email could not be sent.";
            return RedirectToAction(nameof(Index));
        }

        TempData["SuccessMessage"] =
            $"Team leave request {normalizedDecision.ToLowerInvariant()} for {request.EmployeeName}.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<IReadOnlyList<LeaveRequestRecord>> GetTeamRequestsAsync(
        IReadOnlyList<AppUser> teamMembers,
        CancellationToken cancellationToken)
    {
        var teamEmails = teamMembers
            .Select(member => member.Email)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var teamEmployeeIds = teamMembers
            .Select(member => member.EmployeeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var requests = await _leaveRequestStore.GetRequestsAsync(cancellationToken);

        return requests
            .Where(request => teamEmails.Contains(request.EmployeeEmail)
                || teamEmployeeIds.Contains(request.EmployeeId))
            .OrderByDescending(request => request.SubmittedAtUtc)
            .ToList();
    }
}

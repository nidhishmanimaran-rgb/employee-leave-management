namespace EmployeeLeaveManagementSystem.Models;

public class HrDashboardViewModel
{
    public string CurrentUserRole { get; init; } = string.Empty;

    public IReadOnlyList<string> AssignableRoles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<AppUser> PendingRegistrations { get; init; } = Array.Empty<AppUser>();

    public IReadOnlyList<AppUser> ActiveUsers { get; init; } = Array.Empty<AppUser>();

    public int EmployeeCount { get; init; }

    public int ManagerCount { get; init; }

    public string? SearchEmployeeId { get; init; }

    public bool IsSearchActive => !string.IsNullOrWhiteSpace(SearchEmployeeId);

    public AppUser? SelectedUser { get; init; }

    public EmployeeLeaveSummary? SelectedEmployee { get; init; }

    public IReadOnlyList<EmployeeLeaveSummary> EmployeeSummaries { get; init; } = Array.Empty<EmployeeLeaveSummary>();

    public IReadOnlyList<LeaveRequestRecord> Requests { get; init; } = Array.Empty<LeaveRequestRecord>();
}

public class EmployeeLeaveSummary
{
    public string EmployeeId { get; init; } = string.Empty;

    public string EmployeeName { get; init; } = string.Empty;

    public string EmployeeEmail { get; init; } = string.Empty;

    public string EmployeeRole { get; init; } = string.Empty;

    public string EmployeeDepartment { get; init; } = string.Empty;

    public string EmployeePhone { get; init; } = string.Empty;

    public int TotalRequests { get; init; }

    public int AcceptedRequests { get; init; }

    public int RejectedRequests { get; init; }

    public int PendingRequests { get; init; }

    public int ApprovedLeaveDays { get; init; }

    public int RequestedLeaveDays { get; init; }

    public string LatestStatus { get; init; } = string.Empty;

    public DateTimeOffset FirstSubmittedAtUtc { get; init; }

    public DateTimeOffset LastSubmittedAtUtc { get; init; }
}

namespace EmployeeLeaveManagementSystem.Models;

public class EmployeeDashboardViewModel
{
    public AppUser User { get; init; } = new();

    public IReadOnlyList<LeaveRequestRecord> Requests { get; init; } = Array.Empty<LeaveRequestRecord>();

    public int LeaveBalanceDays { get; init; }

    public int ApprovedLeaveDays { get; init; }

    public int PendingRequests { get; init; }

    public int RejectedRequests { get; init; }

    public int RemainingLeaveDays => Math.Max(0, LeaveBalanceDays - ApprovedLeaveDays);
}

public class ManagerDashboardViewModel
{
    public AppUser User { get; init; } = new();

    public IReadOnlyList<AppUser> TeamMembers { get; init; } = Array.Empty<AppUser>();

    public IReadOnlyList<LeaveRequestRecord> TeamRequests { get; init; } = Array.Empty<LeaveRequestRecord>();

    public int PendingRequests { get; init; }

    public int AcceptedRequests { get; init; }

    public int RejectedRequests { get; init; }
}

namespace EmployeeLeaveManagementSystem.Models;

public class LeaveRequestRecord
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset SubmittedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string EmployeeId { get; set; } = string.Empty;

    public string EmployeeRole { get; set; } = string.Empty;

    public string EmployeeDepartment { get; set; } = string.Empty;

    public string? EmployeePhone { get; set; }

    public string EmployeeName { get; set; } = string.Empty;

    public string EmployeeEmail { get; set; } = string.Empty;

    public DateOnly FromDate { get; set; }

    public DateOnly ToDate { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending";

    public DateTimeOffset? DecidedAtUtc { get; set; }

    public string? DecisionComment { get; set; }
}

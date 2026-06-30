using System.Text.Json.Serialization;

namespace EmployeeLeaveManagementSystem.Models;

public class AppUser
{
    public string UserId { get; set; } = string.Empty;

    public string EmployeeId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    [JsonIgnore]
    public string Password { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string Status { get; set; } = "Active";

    public string Department { get; set; } = string.Empty;

    public string JobTitle { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? ManagerEmail { get; set; }

    public int LeaveBalanceDays { get; set; } = 24;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public bool IsActive => Status.Equals("Active", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsPending => Status.Equals("Pending", StringComparison.OrdinalIgnoreCase);
}

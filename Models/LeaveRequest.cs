using System.ComponentModel.DataAnnotations;

namespace EmployeeLeaveManagementSystem.Models;

public class LeaveRequest
{
    [Required]
    [StringLength(100)]
    public string EmployeeId { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string EmployeeRole { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string EmployeeDepartment { get; set; } = string.Empty;

    [Phone]
    [StringLength(20)]
    public string? EmployeePhone { get; set; }

    [Required]
    [StringLength(100)]
    public string EmployeeName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(100)]
    public string EmployeeEmail { get; set; } = string.Empty;

    [Required]
    public DateOnly FromDate { get; set; }

    [Required]
    public DateOnly ToDate { get; set; }

    [Required]
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;
}


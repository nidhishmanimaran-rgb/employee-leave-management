using EmployeeLeaveManagementSystem.Models;
using EmployeeLeaveManagementSystem.Services;
using Microsoft.AspNetCore.Mvc;

namespace EmployeeLeaveManagementSystem.Controllers;

public class AdminController : Controller
{
    private readonly SqlLeaveRequestStore _leaveRequestStore;

    public AdminController(SqlLeaveRequestStore leaveRequestStore)
    {
        _leaveRequestStore = leaveRequestStore;
    }

    // GET: /admin or /Admin
    [HttpGet]
    [Route("admin")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)

    {
        var items = (await _leaveRequestStore.GetRequestsAsync(cancellationToken))
            .Select(request => new LeaveRequestRow
            {
                Id = int.TryParse(request.RequestId, out var id) ? id : 0,
                EmployeeName = request.EmployeeName,
                EmployeeEmail = request.EmployeeEmail,
                FromDate = request.FromDate.ToDateTime(TimeOnly.MinValue),
                ToDate = request.ToDate.ToDateTime(TimeOnly.MinValue),
                Reason = request.Reason,
                Status = request.Status,
                SubmittedAtUtc = request.SubmittedAtUtc
            })
            .ToList();

        return View(items);
    }

    public sealed class LeaveRequestRow
    {
        public int Id { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeEmail { get; set; } = string.Empty;
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset SubmittedAtUtc { get; set; }
    }
}


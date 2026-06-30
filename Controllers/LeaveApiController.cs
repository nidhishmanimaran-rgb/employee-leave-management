using EmployeeLeaveManagementSystem.Models;
using EmployeeLeaveManagementSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace EmployeeLeaveManagementSystem.Controllers;

[ApiController]
[Route("api/LeaveApi")]
public class LeaveApiController : ControllerBase
{
    private const string PublicHolidayWarning = "Unable to verify public holidays at this time.";
    private const string EmailWarning = "Leave request submitted successfully, but the notification email could not be sent.";
    private const string LocalBackupWarning = "Saved a local backup because SQL Server is not reachable.";

    private readonly EmailService _emailService;
    private readonly IConfiguration _config;
    private readonly SqlLeaveRequestStore _sqlLeaveRequestStore;
    private readonly AppUserStore _userStore;
    private readonly PublicHolidayService _publicHolidayService;
    private readonly ILogger<LeaveApiController> _logger;

    public LeaveApiController(
        EmailService emailService,
        IConfiguration config,
        SqlLeaveRequestStore sqlLeaveRequestStore,
        AppUserStore userStore,
        PublicHolidayService publicHolidayService,
        ILogger<LeaveApiController> logger)
    {
        _emailService = emailService;
        _config = config;
        _sqlLeaveRequestStore = sqlLeaveRequestStore;
        _userStore = userStore;
        _publicHolidayService = publicHolidayService;
        _logger = logger;
    }

    [HttpGet]
    [HttpGet("/Leave")]
    public async Task<IActionResult> GetLeaveRequests(CancellationToken cancellationToken)
    {
        try
        {
            var requests = await _sqlLeaveRequestStore.GetRequestsAsync(cancellationToken);
            return Ok(new { success = true, requests });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database connection failed while reading leave requests. Stack: {StackTrace}", ex.StackTrace);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                success = false,
                message = "Database connection failed.",
                detail = $"SQL Error: {ex.Message}"
            });
        }
    }

    [HttpPost]
    [HttpPost("/Leave")]
    public async Task<IActionResult> PostLeaveRequest(
        [FromBody] LeaveRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { success = false, message = "Request body is required." });

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // Step 1: Validate form
        var validationMessage = ValidateRequestDates(request);
        if (validationMessage is not null)
            return BadRequest(new { success = false, message = validationMessage });

        // Step 2: Check leave balance
        IReadOnlyList<LeaveRequestRecord> existingRequests;
        try
        {
            existingRequests = await _sqlLeaveRequestStore.GetRequestsAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Database connection failed before validating leave request. Stack: {StackTrace}", ex.StackTrace);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                success = false,
                message = "Database connection failed.",
                detail = $"SQL Error: {ex.Message}"
            });
        }

        // Step 3: Check overlapping leave
        var businessRuleMessage = ValidateBusinessRules(request, existingRequests);
        if (businessRuleMessage is not null)
            return BadRequest(new { success = false, message = businessRuleMessage });

        // Step 4: Check public holidays (failure should NOT block submission)
        PublicHolidayLookupResult holidayLookup;
        try
        {
            holidayLookup = await _publicHolidayService.GetPublicHolidaysInRangeAsync(
                request.FromDate,
                request.ToDate,
                cancellationToken);
            _logger.LogInformation(
                "Public holiday check result for EmployeeId={EmployeeId}: Available={IsAvailable}, Holidays={Count}",
                request.EmployeeId,
                holidayLookup.IsAvailable,
                holidayLookup.Holidays.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Public holiday verification failed unexpectedly. EmployeeId={EmployeeId} From={FromDate} To={ToDate}. ErrorType={ErrorType}",
                request.EmployeeId,
                request.FromDate,
                request.ToDate,
                ex.GetType().Name);

            holidayLookup = PublicHolidayLookupResult.Unavailable(
                countryCode: "IN",
                sourceEndpoint: string.Empty,
                errorDetail: $"{ex.GetType().Name}: {ex.Message}");
        }

        // Step 5: Save to SQL Server
        LeaveRequestRecord savedRecord;
        try
        {
            savedRecord = await _sqlLeaveRequestStore.InsertRequestAsync(request, cancellationToken);
            _logger.LogInformation(
                "Leave request saved to SQL Server successfully. RequestId={RequestId} EmployeeId={EmployeeId}",
                savedRecord.RequestId,
                savedRecord.EmployeeId);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex,
                "Database write failed when saving leave request. EmployeeId={EmployeeId} From={FromDate} To={ToDate}. Stack={StackTrace}",
                request.EmployeeId,
                request.FromDate,
                request.ToDate,
                ex.StackTrace);

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                success = false,
                message = "Database connection failed.",
                detail = $"SQL Error: {ex.Message}"
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Leave request persistence failed unexpectedly.");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Leave request could not be saved.",
                detail = ex.Message
            });
        }

        var usedLocalBackup = !int.TryParse(savedRecord.RequestId, out _);
        if (usedLocalBackup)
        {
            _logger.LogWarning(
                "Leave request stored in local backup because SQL Server was unavailable. RequestId={RequestId} EmployeeId={EmployeeId}",
                savedRecord.RequestId,
                savedRecord.EmployeeId);
        }

        // Step 6: Send email to HR AFTER successful DB save
        var emailSent = false;
        var emailFailed = false;
        var emailSkipped = _config.GetValue<bool>("Smtp:DisableSending");
        if (!emailSkipped)
        {
            try
            {
                var recipients = GetLeaveNotificationRecipients();
                await Task.WhenAll(recipients.Select(async recipientEmail =>
                {
                    await _emailService.SendNewLeaveRequestEmailAsync(
                        recipientEmail,
                        request.EmployeeId,
                        request.EmployeeRole,
                        request.EmployeeDepartment,
                        request.EmployeePhone,
                        request.EmployeeName,
                        request.EmployeeEmail,
                        request.FromDate,
                        request.ToDate,
                        request.Reason,
                        cancellationToken);

                    _logger.LogInformation(
                        "HR notification email sent to {Recipient} for RequestId={RequestId}",
                        recipientEmail,
                        savedRecord.RequestId);
                }));

                emailSent = true;
            }
            catch (Exception ex)
            {
                emailFailed = true;
                _logger.LogError(ex,
                    "Leave request was saved (RequestId={RequestId}), but the HR notification email could not be sent. EmployeeId={EmployeeId}. ErrorType={ErrorType}",
                    savedRecord.RequestId,
                    savedRecord.EmployeeId,
                    ex.GetType().Name);
            }
        }

        // Step 7&8: Update dashboards happen automatically on next view load
        // Step 9: Show success notification with appropriate warnings
        var message = BuildSuccessMessage(holidayLookup, emailSkipped, emailFailed, usedLocalBackup);

        _logger.LogInformation(
            "Leave request flow complete. RequestId={RequestId} EmployeeId={EmployeeId} EmailSent={EmailSent} HolidayCheck={HolidayAvailable}",
            savedRecord.RequestId,
            savedRecord.EmployeeId,
            emailSent,
            holidayLookup.IsAvailable);

        return Ok(new
        {
            success = true,
            message,
            requestId = savedRecord.RequestId,
            employeeId = savedRecord.EmployeeId,
            employeeName = savedRecord.EmployeeName,
            fromDate = savedRecord.FromDate.ToString("yyyy-MM-dd"),
            toDate = savedRecord.ToDate.ToString("yyyy-MM-dd"),
            status = savedRecord.Status,
            submittedAt = savedRecord.SubmittedAtUtc,
            externalApi = new
            {
                name = "Nager.Date Public Holidays API",
                endpoint = holidayLookup.SourceEndpoint,
                countryCode = holidayLookup.CountryCode,
                enabled = holidayLookup.IsEnabled,
                available = holidayLookup.IsAvailable,
                errorDetail = holidayLookup.ErrorDetail
            },
            publicHolidays = holidayLookup.Holidays.Select(h => new
            {
                date = h.Date.ToString("yyyy-MM-dd"),
                name = h.Name,
                localName = h.LocalName
            }),
            email = new
            {
                sent = emailSent,
                skipped = emailSkipped
            }
        });
    }

    private string? ValidateBusinessRules(
        LeaveRequest request,
        IReadOnlyList<LeaveRequestRecord> existingRequests)
    {
        var employeeRequests = existingRequests
            .Where(existing => BelongsToEmployee(existing, request))
            .ToList();

        // Check overlapping leave
        var overlappingRequest = employeeRequests.FirstOrDefault(existing =>
            !existing.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
            && DatesOverlap(existing.FromDate, existing.ToDate, request.FromDate, request.ToDate));

        if (overlappingRequest is not null)
        {
            var msg = $"This leave overlaps with an existing request ({overlappingRequest.FromDate:dd MMM yyyy} to {overlappingRequest.ToDate:dd MMM yyyy}).";
            _logger.LogWarning("Leave overlap detected. EmployeeId={EmployeeId} ExistingRequestId={ExistingRequestId}", request.EmployeeId, overlappingRequest.RequestId);
            return msg;
        }

        // Check leave balance
        var user = _userStore.GetByEmail(request.EmployeeEmail);
        if (user is null)
            return null;

        var alreadyReservedDays = employeeRequests
            .Where(existing => existing.Status.Equals("Accepted", StringComparison.OrdinalIgnoreCase)
                || existing.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            .Sum(GetInclusiveLeaveDays);
        var requestedDays = GetInclusiveLeaveDays(request.FromDate, request.ToDate);

        _logger.LogInformation(
            "Leave balance check for EmployeeId={EmployeeId}: Reserved={Reserved}, Requested={Requested}, TotalBalance={Balance}",
            request.EmployeeId,
            alreadyReservedDays,
            requestedDays,
            user.LeaveBalanceDays);

        if (alreadyReservedDays + requestedDays > user.LeaveBalanceDays)
        {
            var remaining = Math.Max(0, user.LeaveBalanceDays - alreadyReservedDays);
            return $"Insufficient leave balance. Remaining leave is {remaining} day(s).";
        }

        return null;
    }

    private IReadOnlyList<string> GetLeaveNotificationRecipients()
    {
        var configuredHrEmail = _config["HrEmail"];
        if (!string.IsNullOrWhiteSpace(configuredHrEmail))
            return [configuredHrEmail.Trim()];

        var recipients = _userStore.GetActiveUsers()
            .Where(user => user.Role.Equals("HR", StringComparison.OrdinalIgnoreCase)
                || user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            .Select(user => user.Email)
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return recipients.Count > 0 ? recipients : ["hr@company.com"];
    }

    private static string? ValidateRequestDates(LeaveRequest request)
    {
        if (request.FromDate == default || request.ToDate == default)
            return "Please select a valid start and end date.";

        if (request.ToDate < request.FromDate)
            return "End date must be on or after start date.";

        return null;
    }

    private static bool BelongsToEmployee(LeaveRequestRecord existing, LeaveRequest request)
    {
        return existing.EmployeeEmail.Equals(request.EmployeeEmail, StringComparison.OrdinalIgnoreCase)
            || existing.EmployeeId.Equals(request.EmployeeId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DatesOverlap(DateOnly startA, DateOnly endA, DateOnly startB, DateOnly endB)
    {
        return startA <= endB && startB <= endA;
    }

    private static int GetInclusiveLeaveDays(LeaveRequestRecord request)
    {
        return GetInclusiveLeaveDays(request.FromDate, request.ToDate);
    }

    private static int GetInclusiveLeaveDays(DateOnly fromDate, DateOnly toDate)
    {
        if (fromDate == default || toDate == default || toDate < fromDate)
            return 0;

        return toDate.DayNumber - fromDate.DayNumber + 1;
    }

    private static string BuildSuccessMessage(
        PublicHolidayLookupResult holidayLookup,
        bool emailSkipped,
        bool emailFailed,
        bool localBackupUsed)
    {
        var baseMessage = emailFailed
            ? EmailWarning
            : emailSkipped
                ? "Leave request saved successfully. Email sending is disabled."
                : "Leave request submitted successfully. Email sent to HR successfully.";

        if (localBackupUsed)
            baseMessage = $"{baseMessage} {LocalBackupWarning}";

        if (!holidayLookup.IsAvailable)
            return $"{baseMessage} {PublicHolidayWarning}";

        return baseMessage;
    }
}

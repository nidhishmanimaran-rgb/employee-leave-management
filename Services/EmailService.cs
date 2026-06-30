using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;

namespace EmployeeLeaveManagementSystem.Services;

using System;


/// <summary>
/// Beginner-friendly SMTP email sender using MailKit.
/// </summary>
public class EmailService
{
    private readonly IConfiguration _config;

    public EmailService(IConfiguration config)
    {
        _config = config;
    }

    public async Task SendNewLeaveRequestEmailAsync(
        string hrEmail,
        string employeeId,
        string employeeRole,
        string employeeDepartment,
        string? employeePhone,
        string employeeName,
        string employeeEmail,
        DateOnly fromDate,
        DateOnly toDate,
        string reason,
        CancellationToken cancellationToken = default)
    {
        // Read SMTP settings from configuration (appsettings.json)
        var smtpHost = GetRequiredSetting("Smtp:Host");
        var smtpPort = _config.GetValue<int?>("Smtp:Port")
            ?? throw new InvalidOperationException("SMTP setting 'Smtp:Port' is missing.");
        var smtpUser = GetRequiredSetting("Smtp:Username");
        var smtpPass = GetRequiredSetting("Smtp:Password");
        var fromAddress = GetRequiredSetting("Smtp:FromAddress");
        var fromName = _config["Smtp:FromName"] ?? "EmployeeLeaveSystem";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(hrEmail));

        message.Subject = "New Leave Request";

        // Plain text body for simplicity.
        message.Body = new TextPart("plain")
        {
            Text =
                $"New leave request received!\n\n" +
                $"Employee ID: {employeeId}\n" +
                $"Role: {employeeRole}\n" +
                $"Department: {employeeDepartment}\n" +
                $"Phone: {GetDisplayValue(employeePhone)}\n" +
                $"Employee Name: {employeeName}\n" +
                $"Employee Email: {employeeEmail}\n" +
                $"From Date: {fromDate:yyyy-MM-dd}\n" +
                $"To Date: {toDate:yyyy-MM-dd}\n" +
                $"Reason: {reason}\n"
        };

        using var smtp = new SmtpClient();

        // For many SMTP servers, STARTTLS is common.
        await smtp.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls, cancellationToken);
        await smtp.AuthenticateAsync(smtpUser, smtpPass, cancellationToken);

        await smtp.SendAsync(message, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);
    }

    public async Task SendLeaveDecisionEmailAsync(
        string employeeEmail,
        string employeeId,
        string employeeRole,
        string employeeDepartment,
        string employeeName,
        string decision,
        DateOnly fromDate,
        DateOnly toDate,
        string reason,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        var smtpHost = GetRequiredSetting("Smtp:Host");
        var smtpPort = _config.GetValue<int?>("Smtp:Port")
            ?? throw new InvalidOperationException("SMTP setting 'Smtp:Port' is missing.");
        var smtpUser = GetRequiredSetting("Smtp:Username");
        var smtpPass = GetRequiredSetting("Smtp:Password");
        var fromAddress = GetRequiredSetting("Smtp:FromAddress");
        var fromName = _config["Smtp:FromName"] ?? "EmployeeLeaveSystem";
        var normalizedDecision = decision.Equals("Accepted", StringComparison.OrdinalIgnoreCase)
            ? "accepted"
            : "rejected";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(employeeEmail));
        message.Subject = $"Leave Request {normalizedDecision.ToUpperInvariant()}";

        var commentText = string.IsNullOrWhiteSpace(comment)
            ? string.Empty
            : $"\nHR Comment: {comment.Trim()}\n";

        message.Body = new TextPart("plain")
        {
            Text =
                $"Hello {employeeName},\n\n" +
                $"Your leave request has been {normalizedDecision}.\n\n" +
                $"Employee ID: {employeeId}\n" +
                $"Role: {employeeRole}\n" +
                $"Department: {employeeDepartment}\n" +
                $"From Date: {fromDate:yyyy-MM-dd}\n" +
                $"To Date: {toDate:yyyy-MM-dd}\n" +
                $"Reason: {reason}\n" +
                commentText +
                "\nRegards,\nHR Team\n"
        };

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls, cancellationToken);
        await smtp.AuthenticateAsync(smtpUser, smtpPass, cancellationToken);
        await smtp.SendAsync(message, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);
    }

    public Task SendNewEmployeeRegistrationEmailAsync(
        string hrEmail,
        string employeeName,
        string employeeEmail,
        string department,
        string designation,
        string? approvalUrl,
        CancellationToken cancellationToken = default)
    {
        var approvalText = string.IsNullOrWhiteSpace(approvalUrl)
            ? "Open the HR Dashboard to approve or reject this registration."
            : $"Click below to approve:\n{approvalUrl}";

        var body =
            $"Employee Name:\n{employeeName}\n\n" +
            $"Email:\n{employeeEmail}\n\n" +
            $"Department:\n{department}\n\n" +
            $"Designation:\n{designation}\n\n" +
            $"{approvalText}\n";

        return SendPlainTextEmailAsync(hrEmail, "New Employee Registration", body, cancellationToken);
    }

    public Task SendAccountApprovedEmailAsync(
        string employeeEmail,
        string employeeName,
        CancellationToken cancellationToken = default)
    {
        var body =
            $"Hello {employeeName},\n\n" +
            "Your Employee Leave Management account has been approved.\n\n" +
            $"Login Email:\n{employeeEmail}\n\n" +
            "You can now log in.\n\n" +
            "Regards\nHR Team\n";

        return SendPlainTextEmailAsync(employeeEmail, "Account Approved", body, cancellationToken);
    }

    public Task SendRegistrationRejectedEmailAsync(
        string employeeEmail,
        string employeeName,
        CancellationToken cancellationToken = default)
    {
        var body =
            $"Hello {employeeName},\n\n" +
            "Your Employee Leave Management account registration was rejected.\n\n" +
            "Please contact HR if you need more information.\n\n" +
            "Regards\nHR Team\n";

        return SendPlainTextEmailAsync(employeeEmail, "Registration Rejected", body, cancellationToken);
    }

    private async Task SendPlainTextEmailAsync(
        string toEmail,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        var smtpHost = GetRequiredSetting("Smtp:Host");
        var smtpPort = _config.GetValue<int?>("Smtp:Port")
            ?? throw new InvalidOperationException("SMTP setting 'Smtp:Port' is missing.");
        var smtpUser = GetRequiredSetting("Smtp:Username");
        var smtpPass = GetRequiredSetting("Smtp:Password");
        var fromAddress = GetRequiredSetting("Smtp:FromAddress");
        var fromName = _config["Smtp:FromName"] ?? "EmployeeLeaveSystem";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls, cancellationToken);
        await smtp.AuthenticateAsync(smtpUser, smtpPass, cancellationToken);
        await smtp.SendAsync(message, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);
    }

    private string GetRequiredSetting(string key)
    {
        var value = _config[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"SMTP setting '{key}' is missing.");

        return value;
    }

    private static string GetDisplayValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Not provided" : value.Trim();
    }
}


using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EmployeeLeaveManagementSystem.Models;

namespace EmployeeLeaveManagementSystem.Services;

public class LeaveRequestStore
{
    private readonly string _requestsFilePath;
    private readonly string _decisionsFilePath;

    public LeaveRequestStore(IWebHostEnvironment environment)
    {
        var dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);

        _requestsFilePath = Path.Combine(dataDirectory, "leave-requests.jsonl");
        _decisionsFilePath = Path.Combine(dataDirectory, "leave-decisions.jsonl");

        EnsureFileExists(_requestsFilePath);
        EnsureFileExists(_decisionsFilePath);
    }

    public async Task<LeaveRequestRecord> SaveRequestAsync(LeaveRequest request, CancellationToken cancellationToken = default)
    {
        var record = new LeaveRequestRecord
        {
            RequestId = Guid.NewGuid().ToString("N"),
            SubmittedAtUtc = DateTimeOffset.UtcNow,
            EmployeeId = request.EmployeeId,
            EmployeeRole = request.EmployeeRole,
            EmployeeDepartment = request.EmployeeDepartment,
            EmployeePhone = request.EmployeePhone,
            EmployeeName = request.EmployeeName,
            EmployeeEmail = request.EmployeeEmail,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            Reason = request.Reason,
            Status = "Pending"
        };

        var line = JsonSerializer.Serialize(record) + Environment.NewLine;
        await File.AppendAllTextAsync(_requestsFilePath, line, cancellationToken);

        return record;
    }

    public async Task<IReadOnlyList<LeaveRequestRecord>> GetRequestsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_requestsFilePath))
            return Array.Empty<LeaveRequestRecord>();

        var decisions = await GetLatestDecisionsAsync(cancellationToken);
        var lines = await File.ReadAllLinesAsync(_requestsFilePath, cancellationToken);
        var records = new List<LeaveRequestRecord>();

        foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            var record = ParseRequestLine(line);
            if (record is null)
                continue;

            if (decisions.TryGetValue(record.RequestId, out var decision))
            {
                record.Status = decision.Status;
                record.DecidedAtUtc = decision.DecidedAtUtc;
                record.DecisionComment = decision.Comment;
            }

            records.Add(record);
        }

        return records
            .OrderByDescending(record => record.SubmittedAtUtc)
            .ToList();
    }

    public async Task<LeaveRequestRecord?> GetRequestAsync(string requestId, CancellationToken cancellationToken = default)
    {
        var requests = await GetRequestsAsync(cancellationToken);
        return requests.FirstOrDefault(request => request.RequestId == requestId);
    }

    public async Task SaveDecisionAsync(
        string requestId,
        string status,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        var decision = new LeaveDecisionRecord
        {
            RequestId = requestId,
            Status = status,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            DecidedAtUtc = DateTimeOffset.UtcNow
        };

        var line = JsonSerializer.Serialize(decision) + Environment.NewLine;
        await File.AppendAllTextAsync(_decisionsFilePath, line, cancellationToken);
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await File.WriteAllTextAsync(_requestsFilePath, string.Empty, cancellationToken);
        await File.WriteAllTextAsync(_decisionsFilePath, string.Empty, cancellationToken);
    }

    private async Task<Dictionary<string, LeaveDecisionRecord>> GetLatestDecisionsAsync(CancellationToken cancellationToken)
    {
        var decisions = new Dictionary<string, LeaveDecisionRecord>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_decisionsFilePath))
            return decisions;

        var lines = await File.ReadAllLinesAsync(_decisionsFilePath, cancellationToken);
        foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            var decision = JsonSerializer.Deserialize<LeaveDecisionRecord>(line);
            if (decision is null || string.IsNullOrWhiteSpace(decision.RequestId))
                continue;

            decisions[decision.RequestId] = decision;
        }

        return decisions;
    }

    private static LeaveRequestRecord? ParseRequestLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            return new LeaveRequestRecord
            {
                RequestId = GetString(root, "RequestId") ?? CreateLegacyRequestId(line),
                SubmittedAtUtc = GetDateTimeOffset(root, "SubmittedAtUtc") ?? DateTimeOffset.MinValue,
                EmployeeId = GetString(root, "EmployeeId") ?? string.Empty,
                EmployeeRole = GetString(root, "EmployeeRole") ?? string.Empty,
                EmployeeDepartment = GetString(root, "EmployeeDepartment") ?? string.Empty,
                EmployeePhone = GetString(root, "EmployeePhone"),
                EmployeeName = GetString(root, "EmployeeName") ?? string.Empty,
                EmployeeEmail = GetString(root, "EmployeeEmail") ?? string.Empty,
                FromDate = GetDateOnly(root, "FromDate") ?? default,
                ToDate = GetDateOnly(root, "ToDate") ?? default,
                Reason = GetString(root, "Reason") ?? string.Empty,
                Status = GetString(root, "Status") ?? "Pending"
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static DateOnly? GetDateOnly(JsonElement root, string propertyName)
    {
        var value = GetString(root, propertyName);
        return DateOnly.TryParse(value, out var date) ? date : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement root, string propertyName)
    {
        var value = GetString(root, propertyName);
        return DateTimeOffset.TryParse(value, out var date) ? date : null;
    }

    private static string CreateLegacyRequestId(string line)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(line));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void EnsureFileExists(string filePath)
    {
        if (File.Exists(filePath))
            return;

        using var _ = File.Create(filePath);
    }

    private sealed class LeaveDecisionRecord
    {
        public string RequestId { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public DateTimeOffset DecidedAtUtc { get; set; }

        public string? Comment { get; set; }
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EmployeeLeaveManagementSystem.Models;

namespace EmployeeLeaveManagementSystem.Services;

public class AppUserStore
{
    private static readonly string[] ValidRoles = ["Employee", "Manager", "HR", "Admin"];
    private readonly IConfiguration _config;
    private readonly string _usersFilePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly object _syncRoot = new();

    public AppUserStore(IConfiguration config, IWebHostEnvironment environment)
    {
        _config = config;

        var dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);

        _usersFilePath = Path.Combine(dataDirectory, "app-users.json");
        EnsureSeeded();
    }

    public IReadOnlyList<AppUser> GetUsers()
    {
        lock (_syncRoot)
        {
            return ReadUsersUnlocked()
                .OrderBy(user => user.Name)
                .ToList();
        }
    }

    public IReadOnlyList<AppUser> GetActiveUsers()
    {
        return GetUsers()
            .Where(user => user.IsActive)
            .OrderBy(user => user.Name)
            .ToList();
    }

    public IReadOnlyList<AppUser> GetPendingRegistrations()
    {
        return GetUsers()
            .Where(user => user.IsPending)
            .OrderBy(user => user.CreatedAtUtc)
            .ToList();
    }

    public AppUser? ValidateUser(string email, string password)
    {
        var normalizedEmail = NormalizeEmail(email);

        return GetUsers().FirstOrDefault(user =>
            user.IsActive
            && user.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase)
            && VerifyPassword(user, password));
    }

    public AppUser? GetByEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var normalizedEmail = NormalizeEmail(email);

        return GetUsers().FirstOrDefault(user =>
            user.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<AppUser> GetTeamMembers(string managerEmail)
    {
        return GetActiveUsers()
            .Where(user => user.ManagerEmail?.Equals(managerEmail, StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(user => user.Name)
            .ToList();
    }

    public UserStoreResult RegisterPending(RegisterViewModel model)
    {
        lock (_syncRoot)
        {
            var users = ReadUsersUnlocked();
            var normalizedEmail = NormalizeEmail(model.Email);

            if (users.Any(user => user.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase)))
                return UserStoreResult.Fail("An account already exists for this email address.");

            var user = new AppUser
            {
                UserId = GenerateUserId(),
                EmployeeId = normalizedEmail,
                Name = model.Name.Trim(),
                Email = normalizedEmail,
                PasswordHash = HashPassword(model.Password),
                Role = "Employee",
                Status = "Pending",
                Department = model.Department.Trim(),
                JobTitle = model.Designation.Trim(),
                LeaveBalanceDays = 12,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            users.Add(user);
            WriteUsersUnlocked(users);

            return UserStoreResult.Ok("Registration submitted for HR approval.", user);
        }
    }

    public UserStoreResult ApproveRegistration(string userId)
    {
        lock (_syncRoot)
        {
            var users = ReadUsersUnlocked();
            var user = FindUser(users, userId);
            if (user is null)
                return UserStoreResult.Fail("Employee registration was not found.");

            if (!user.IsPending)
                return UserStoreResult.Fail("Only pending registrations can be approved.");

            user.Status = "Active";
            user.Role = "Employee";
            user.LeaveBalanceDays = user.LeaveBalanceDays <= 0 ? 12 : user.LeaveBalanceDays;

            WriteUsersUnlocked(users);
            return UserStoreResult.Ok("Employee account approved.", user);
        }
    }

    public UserStoreResult RejectRegistration(string userId)
    {
        lock (_syncRoot)
        {
            var users = ReadUsersUnlocked();
            var user = FindUser(users, userId);
            if (user is null)
                return UserStoreResult.Fail("Employee registration was not found.");

            if (!user.IsPending)
                return UserStoreResult.Fail("Only pending registrations can be rejected.");

            user.Status = "Rejected";
            WriteUsersUnlocked(users);

            return UserStoreResult.Ok("Employee registration rejected.", user);
        }
    }

    public UserStoreResult UpdateRole(string userId, string newRole, string? currentUserRole)
    {
        var normalizedRole = NormalizeRole(newRole);
        if (normalizedRole is null)
            return UserStoreResult.Fail("Please choose a valid role.");

        if (!CanAssignRole(currentUserRole, normalizedRole))
            return UserStoreResult.Fail("Your current role is not allowed to assign that role.");

        lock (_syncRoot)
        {
            var users = ReadUsersUnlocked();
            var user = FindUser(users, userId);
            if (user is null)
                return UserStoreResult.Fail("Employee account was not found.");

            if (!user.IsActive)
                return UserStoreResult.Fail("Only active employees can have roles changed.");

            user.Role = normalizedRole;
            WriteUsersUnlocked(users);

            return UserStoreResult.Ok($"Role updated to {normalizedRole}.", user);
        }
    }

    public static IReadOnlyList<string> GetAssignableRoles(string? currentUserRole)
    {
        if (string.Equals(currentUserRole, "Admin", StringComparison.OrdinalIgnoreCase))
            return ValidRoles;

        if (string.Equals(currentUserRole, "HR", StringComparison.OrdinalIgnoreCase))
            return ["Employee", "Manager"];

        if (string.Equals(currentUserRole, "Manager", StringComparison.OrdinalIgnoreCase))
            return ["Employee"];

        return Array.Empty<string>();
    }

    private void EnsureSeeded()
    {
        lock (_syncRoot)
        {
            var users = ReadUsersUnlocked();
            var changed = false;

            foreach (var seedUser in GetConfiguredUsers())
            {
                NormalizeSeedUser(seedUser, users.Count);

                if (users.Any(user => user.Email.Equals(seedUser.Email, StringComparison.OrdinalIgnoreCase)))
                    continue;

                users.Add(seedUser);
                changed = true;
            }

            if (changed || !File.Exists(_usersFilePath))
                WriteUsersUnlocked(users);
        }
    }

    private List<AppUser> ReadUsersUnlocked()
    {
        if (!File.Exists(_usersFilePath))
            return [];

        var json = File.ReadAllText(_usersFilePath);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<AppUser>>(json, _jsonOptions) ?? [];
        }
        catch (JsonException)
        {
            var backupPath = $"{_usersFilePath}.{DateTime.UtcNow:yyyyMMddHHmmss}.corrupt";
            File.Copy(_usersFilePath, backupPath, overwrite: true);
            return [];
        }
    }

    private void WriteUsersUnlocked(List<AppUser> users)
    {
        var normalizedUsers = users
            .Select((user, index) =>
            {
                NormalizeSeedUser(user, index);
                return user;
            })
            .OrderBy(user => user.Name)
            .ToList();

        File.WriteAllText(_usersFilePath, JsonSerializer.Serialize(normalizedUsers, _jsonOptions));
    }

    private IReadOnlyList<AppUser> GetConfiguredUsers()
    {
        var users = _config.GetSection("AppUsers").Get<List<AppUser>>();
        return users is { Count: > 0 } ? users : GetFallbackUsers();
    }

    private static AppUser? FindUser(IEnumerable<AppUser> users, string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        var normalized = userId.Trim();

        return users.FirstOrDefault(user =>
            user.UserId.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || user.EmployeeId.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || user.Email.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static void NormalizeSeedUser(AppUser user, int index)
    {
        user.UserId = string.IsNullOrWhiteSpace(user.UserId) ? GenerateUserId() : user.UserId.Trim();
        user.EmployeeId = string.IsNullOrWhiteSpace(user.EmployeeId)
            ? GenerateEmployeeId(index)
            : user.EmployeeId.Trim();
        user.Name = user.Name.Trim();
        user.Email = NormalizeEmail(user.Email);
        user.Role = NormalizeRole(user.Role) ?? "Employee";
        user.Status = string.IsNullOrWhiteSpace(user.Status) ? "Active" : user.Status.Trim();
        user.Department = user.Department.Trim();
        user.JobTitle = user.JobTitle.Trim();
        user.ManagerEmail = string.IsNullOrWhiteSpace(user.ManagerEmail) ? null : NormalizeEmail(user.ManagerEmail);
        user.LeaveBalanceDays = user.LeaveBalanceDays <= 0 ? 12 : user.LeaveBalanceDays;
        user.CreatedAtUtc = user.CreatedAtUtc == default ? DateTimeOffset.UtcNow : user.CreatedAtUtc;

        if (string.IsNullOrWhiteSpace(user.PasswordHash) && !string.IsNullOrWhiteSpace(user.Password))
            user.PasswordHash = HashPassword(user.Password);
    }

    private static string GenerateUserId()
    {
        return $"USR{Guid.NewGuid():N}";
    }

    private static string GenerateEmployeeId(IReadOnlyCollection<AppUser> users)
    {
        var nextNumber = users.Count + 1001;
        var employeeId = GenerateEmployeeId(nextNumber);

        while (users.Any(user => user.EmployeeId.Equals(employeeId, StringComparison.OrdinalIgnoreCase)))
        {
            nextNumber++;
            employeeId = GenerateEmployeeId(nextNumber);
        }

        return employeeId;
    }

    private static string GenerateEmployeeId(int number)
    {
        return $"EMP{number:D4}";
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static string? NormalizeRole(string role)
    {
        return ValidRoles.FirstOrDefault(validRole =>
            validRole.Equals(role.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool CanAssignRole(string? currentUserRole, string roleToAssign)
    {
        return GetAssignableRoles(currentUserRole)
            .Contains(roleToAssign, StringComparer.OrdinalIgnoreCase);
    }

    private static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool VerifyPassword(AppUser user, string password)
    {
        if (!string.IsNullOrWhiteSpace(user.PasswordHash))
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(user.PasswordHash),
                Encoding.UTF8.GetBytes(HashPassword(password)));

        return user.Password == password;
    }

    private static IReadOnlyList<AppUser> GetFallbackUsers()
    {
        return new List<AppUser>
        {
            new()
            {
                EmployeeId = "ADM1000",
                Name = "Nidhish Kumaran",
                Email = "nidhishmanimaran@gmail.com",
                Password = "12345678",
                Role = "Admin",
                Department = "Administration",
                JobTitle = "System Administrator",
                Phone = "9876543200",
                LeaveBalanceDays = 30
            },
            new()
            {
                EmployeeId = "EMP1001",
                Name = "Ananya Rao",
                Email = "employee@company.com",
                Password = "12345678",
                Role = "Employee",
                Department = "Operations",
                JobTitle = "Operations Executive",
                Phone = "9876543210",
                ManagerEmail = "manager@company.com",
                LeaveBalanceDays = 24
            },
            new()
            {
                EmployeeId = "nidhishkumaranm@gmail.com",
                Name = "Employee Name",
                Email = "nidhishkumaranm@gmail.com",
                Password = "12345678",
                Role = "Employee",
                Status = "Active",
                Department = "IT",
                JobTitle = "Employee",
                Phone = "9876543201",
                ManagerEmail = "manager@company.com",
                LeaveBalanceDays = 12
            },
            new()
            {
                EmployeeId = "MGR1001",
                Name = "Karthik Menon",
                Email = "manager@company.com",
                Password = "12345678",
                Role = "Manager",
                Department = "Operations",
                JobTitle = "Operations Manager",
                Phone = "9876543211",
                LeaveBalanceDays = 30
            },
            new()
            {
                EmployeeId = "HR1001",
                Name = "Meera HR",
                Email = "hr@company.com",
                Password = "12345678",
                Role = "HR",
                Department = "Human Resources",
                JobTitle = "HR Specialist",
                Phone = "9876543212",
                LeaveBalanceDays = 30
            }
        };
    }
}

public sealed record UserStoreResult(bool Succeeded, string Message, AppUser? User)
{
    public static UserStoreResult Ok(string message, AppUser user)
    {
        return new UserStoreResult(true, message, user);
    }

    public static UserStoreResult Fail(string message)
    {
        return new UserStoreResult(false, message, null);
    }
}

using EmployeeLeaveManagementSystem.Models;

namespace EmployeeLeaveManagementSystem.Services;

public static class AuthSession
{
    public const string UserEmailKey = "UserEmail";
    public const string UserRoleKey = "UserRole";
    public const string UserNameKey = "UserName";
    public const string EmployeeIdKey = "EmployeeId";
    public const string LegacyHrAuthenticatedKey = "HrDashboardAuthenticated";

    public static void SignIn(ISession session, AppUser user)
    {
        session.SetString(UserEmailKey, user.Email);
        session.SetString(UserRoleKey, user.Role);
        session.SetString(UserNameKey, user.Name);
        session.SetString(EmployeeIdKey, user.EmployeeId);

        if (IsHrRole(user.Role))
            session.SetString(LegacyHrAuthenticatedKey, "true");
        else
            session.Remove(LegacyHrAuthenticatedKey);
    }

    public static void SignOut(ISession session)
    {
        session.Remove(UserEmailKey);
        session.Remove(UserRoleKey);
        session.Remove(UserNameKey);
        session.Remove(EmployeeIdKey);
        session.Remove(LegacyHrAuthenticatedKey);
    }

    public static bool IsSignedIn(ISession session)
    {
        return !string.IsNullOrWhiteSpace(session.GetString(UserEmailKey));
    }

    public static string? GetEmail(ISession session)
    {
        return session.GetString(UserEmailKey);
    }

    public static string? GetRole(ISession session)
    {
        return session.GetString(UserRoleKey);
    }

    public static bool IsRole(ISession session, string role)
    {
        return string.Equals(GetRole(session), role, StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanAccessHr(ISession session)
    {
        return IsHrRole(GetRole(session)) || session.GetString(LegacyHrAuthenticatedKey) == "true";
    }

    public static bool IsHrRole(string? role)
    {
        return string.Equals(role, "HR", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
    }
}

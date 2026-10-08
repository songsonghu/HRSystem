namespace HRSystem.Application.Security;

/// <summary>A permission that can be granted to a role.</summary>
public sealed record PermissionDefinition(string Name, string Group, string Description);

/// <summary>
/// The fixed catalogue of permissions. Roles are configurable at runtime; each
/// role is granted a subset of these, stored as role claims of <see cref="ClaimType"/>.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string EmployeesManage = "employees.manage";
    public const string AccountRequestsManage = "account-requests.manage";
    public const string DeparturesManage = "departures.manage";
    public const string OffboardingExport = "offboarding.export";
    public const string TasksProcess = "tasks.process";
    public const string SystemJobs = "system.jobs";

    public static readonly IReadOnlyList<PermissionDefinition> All = new[]
    {
        new PermissionDefinition(UsersManage, "System", "Manage users (create, edit, disable, delete, reset password)"),
        new PermissionDefinition(RolesManage, "System", "Manage roles and their permissions"),
        new PermissionDefinition(SystemJobs, "System", "View the background jobs dashboard"),
        new PermissionDefinition(EmployeesManage, "HR", "Manage employee records"),
        new PermissionDefinition(AccountRequestsManage, "HR", "Create, submit and track account requests"),
        new PermissionDefinition(DeparturesManage, "HR", "Create, submit and finalize departure requests"),
        new PermissionDefinition(OffboardingExport, "HR", "View and export an employee's accounts for offboarding"),
        new PermissionDefinition(TasksProcess, "Department", "Process account and departure tasks assigned to me"),
    };

    public static bool IsDefined(string name) => All.Any(p => p.Name == name);
}

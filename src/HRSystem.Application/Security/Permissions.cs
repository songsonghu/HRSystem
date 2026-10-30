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
    public const string DepartmentsManage = "departments.manage";
    public const string OnboardingManage = "onboarding.manage";
    public const string AccountRequestsManage = "account-requests.manage";
    public const string DeparturesManage = "departures.manage";
    public const string DeparturesApprove = "departures.approve";
    public const string OffboardingExport = "offboarding.export";
    public const string SystemJobs = "system.jobs";
    public const string ChecklistApproversManage = "checklist-approvers.manage";

    public static readonly IReadOnlyList<PermissionDefinition> All = new[]
    {
        new PermissionDefinition(UsersManage, "System", "Manage users (create, edit, disable, delete, reset password)"),
        new PermissionDefinition(RolesManage, "System", "Manage roles and their permissions"),
        new PermissionDefinition(SystemJobs, "System", "View the background jobs dashboard"),
        new PermissionDefinition(ChecklistApproversManage, "System", "Set the handlers and approvers of each department on Templates for Accounts and Templates for Departure"),
        new PermissionDefinition(EmployeesManage, "HR", "Manage employee records"),
        new PermissionDefinition(DepartmentsManage, "HR", "Manage departments and department managers"),
        new PermissionDefinition(AccountRequestsManage, "HR", "Create, submit and track account requests"),
        new PermissionDefinition(OnboardingManage, "HR", "Create, submit and finalize onboarding requests; manage onboarding checklists"),
        new PermissionDefinition(DeparturesManage, "HR", "Create, submit and cancel departure requests; manage departure checklists"),
        new PermissionDefinition(DeparturesApprove, "HR", "Give departures the last approval (HR / management) after the department head, or return them"),
        new PermissionDefinition(OffboardingExport, "HR", "View and export an employee's accounts for offboarding"),
    };

    public static bool IsDefined(string name) => All.Any(p => p.Name == name);
}

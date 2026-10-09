using System.Net;
using HRSystem.Application.Security;
using HRSystem.Domain.Enums;
using HRSystem.Infrastructure.Identity;
using HRSystem.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Tests.Integration;

public class StartupAndAccessTests : IntegrationTest
{
    public StartupAndAccessTests(TestApp app) : base(app) { }

    [SkippableFact]
    public async Task Empty_database_is_migrated_and_seeded()
    {
        RequireDatabase();

        var hrPermissions = await Data.ReadAsync(db =>
            (from c in db.RoleClaims
             join r in db.Roles on c.RoleId equals r.Id
             where r.Name == Roles.HR && c.ClaimType == Permissions.ClaimType
             select c.ClaimValue!).ToListAsync());
        Assert.Equal(
            new[]
            {
                Permissions.AccountRequestsManage, Permissions.DepartmentsManage, Permissions.DeparturesManage,
                Permissions.EmployeesManage, Permissions.OffboardingExport, Permissions.OnboardingManage
            },
            hrPermissions.OrderBy(p => p, StringComparer.Ordinal));

        var admins = await Data.ReadAsync(db =>
            (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where r.Name == Roles.Admin select ur.UserId).CountAsync());
        Assert.Equal(1, admins);

        var templates = await Data.ReadAsync(db => db.ChecklistTemplates.Include(t => t.Items).ToListAsync());
        Assert.Equal(5, templates.Count(t => t.Kind == ChecklistKind.Departure));
        Assert.Equal(4, templates.Count(t => t.Kind == ChecklistKind.Onboarding));
        Assert.All(templates.Where(t => t.Kind == ChecklistKind.Onboarding).SelectMany(t => t.Items).Where(i => i.Description == "Others"),
            item => Assert.False(item.IsRequired));
    }

    [SkippableFact]
    public async Task Anonymous_users_are_sent_to_login()
    {
        RequireDatabase();
        var response = await App.CreateClient(new() { AllowAutoRedirect = false }).GetAsync("/Employees");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", response.Headers.Location!.ToString());
    }

    [SkippableFact]
    public async Task Pages_follow_role_permissions()
    {
        RequireDatabase();
        var hr = await Data.PersonAsync(null, Roles.HR);
        var client = await SignedInClientAsync(hr.Email, TestData.Password);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Employees")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Onboarding")).StatusCode);
        var users = await client.GetAsync("/Users");
        Assert.Equal(HttpStatusCode.Redirect, users.StatusCode);
        Assert.Contains("AccessDenied", users.Headers.Location!.ToString());
    }

    [SkippableFact]
    public async Task Task_pages_are_open_to_users_without_roles()
    {
        RequireDatabase();
        var plain = await Data.PersonAsync(null);
        var client = await SignedInClientAsync(plain.Email, TestData.Password);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/MyTasks")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/ChecklistTasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Employees")).StatusCode);
    }

    [SkippableFact]
    public async Task Admin_can_open_administration_pages()
    {
        RequireDatabase();
        var client = await SignedInClientAsync("admin@hrsystem.local", "Admin@12345");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Roles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/ChecklistTemplates?kind=Departure")).StatusCode);
    }
}

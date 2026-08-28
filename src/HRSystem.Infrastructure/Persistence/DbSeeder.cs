using HRSystem.Domain.Entities;
using HRSystem.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRSystem.Infrastructure.Persistence;

/// <summary>
/// Applies pending migrations and seeds baseline data: roles, a default admin,
/// the five responsible departments, and the standard account types.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<AppDbContext>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        await db.Database.MigrateAsync();

        // 1) Roles
        foreach (var role in new[] { Roles.Admin, Roles.HR, Roles.DeptHead })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // 2) Default admin account
        const string adminEmail = "admin@hrsystem.local";
        if (await userManager.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                FullName = "System Administrator"
            };
            await userManager.CreateAsync(admin, "Admin@12345");
            await userManager.AddToRoleAsync(admin, Roles.Admin);
        }

        // 3) Departments (each with a placeholder head user email as HeadUserId).
        if (!await db.Departments.AnyAsync())
        {
            db.Departments.AddRange(
                new Department { Name = "IT Infrastructure", Code = "ITINFRA", HeadUserId = "itinfra.head@hrsystem.local" },
                new Department { Name = "IT Mail",           Code = "ITMAIL",  HeadUserId = "itmail.head@hrsystem.local" },
                new Department { Name = "Trading Systems",   Code = "TRADE",   HeadUserId = "trade.head@hrsystem.local" },
                new Department { Name = "Back Office",       Code = "BACKOFC", HeadUserId = "backoffice.head@hrsystem.local" },
                new Department { Name = "Network Security",  Code = "NETSEC",  HeadUserId = "netsec.head@hrsystem.local" });
            await db.SaveChangesAsync();
        }

        // 4) Account types mapped to responsible departments.
        if (!await db.AccountTypes.AnyAsync())
        {
            var depts = await db.Departments.ToDictionaryAsync(d => d.Code!, d => d.Id);
            db.AccountTypes.AddRange(
                new AccountType { Code = "PCDomain", Name = "PC Domain Account", ResponsibleDeptId = depts["ITINFRA"], SortOrder = 1 },
                new AccountType { Code = "Email",    Name = "E-mail Account",    ResponsibleDeptId = depts["ITMAIL"],  SortOrder = 2 },
                new AccountType { Code = "Ayers",    Name = "Ayers Account",     ResponsibleDeptId = depts["TRADE"],   SortOrder = 3 },
                new AccountType { Code = "IBO",      Name = "IBO Account",       ResponsibleDeptId = depts["BACKOFC"], SortOrder = 4 },
                new AccountType { Code = "VPN",      Name = "VPN Account",       ResponsibleDeptId = depts["NETSEC"],  SortOrder = 5 });
            await db.SaveChangesAsync();
        }
    }
}

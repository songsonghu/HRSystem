using HRSystem.Domain.Entities;
using HRSystem.Domain.Enums;
using HRSystem.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRSystem.Infrastructure.Persistence;

/// <summary>
/// Applies pending migrations and seeds baseline data: roles, a default admin,
/// the responsible departments, and the standard account types mirroring the
/// two paper "Staff Requisition Form" layouts (Staff, and AE/Sales/SA).
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
        // These mirror the sections printed on the two paper requisition forms.
        if (!await db.Departments.AnyAsync())
        {
            db.Departments.AddRange(
                new Department { Name = "Client Service & Internet Trading", Code = "CSIT",   HeadUserId = "csit.head@hrsystem.local" },
                new Department { Name = "Credit Department",                 Code = "CREDIT", HeadUserId = "credit.head@hrsystem.local" },
                new Department { Name = "IT Department",                     Code = "IT",      HeadUserId = "it.head@hrsystem.local" },
                new Department { Name = "HR & Administration Department",    Code = "HR",      HeadUserId = "hr.head@hrsystem.local" });
            await db.SaveChangesAsync();
        }

        // 4) Account types mapped to responsible departments, tagged with the
        // requisition form(s) they appear on (Staff / AE / Both).
        if (!await db.AccountTypes.AnyAsync())
        {
            var depts = await db.Departments.ToDictionaryAsync(d => d.Code!, d => d.Id);
            var s = AccountTypeAudience.StaffOnly;
            var ae = AccountTypeAudience.AEOnly;
            var both = AccountTypeAudience.Both;
            int order = 0;

            db.AccountTypes.AddRange(
                // --- Client Service & Internet Trading (AE/Sales/SA form only) ---
                new AccountType { Code = "AECode",       Name = "AE Code",                                  ResponsibleDeptId = depts["CSIT"],   Audience = ae,   SortOrder = ++order },
                new AccountType { Code = "CSITOthers",   Name = "Others",                                   ResponsibleDeptId = depts["CSIT"],   Audience = ae,   SortOrder = ++order, RequiresDetail = true, DetailLabel = "Specify" },

                // --- Credit Department (both forms) ---
                new AccountType { Code = "TradingSystem",Name = "Trading System (Ayers/Sharp Point)",       ResponsibleDeptId = depts["CREDIT"], Audience = both, SortOrder = ++order },
                new AccountType { Code = "CreditOthers", Name = "Others",                                   ResponsibleDeptId = depts["CREDIT"], Audience = both, SortOrder = ++order, RequiresDetail = true, DetailLabel = "Specify" },

                // --- IT Department (Staff form) ---
                new AccountType { Code = "PC",           Name = "PC",                                       ResponsibleDeptId = depts["IT"],     Audience = both, SortOrder = ++order },
                new AccountType { Code = "IBOSystem",    Name = "IBO System (HK/SG/US)",                    ResponsibleDeptId = depts["IT"],     Audience = s,    SortOrder = ++order },
                new AccountType { Code = "AFEEquity",    Name = "AFE Equity Stock Option Back Office System",ResponsibleDeptId = depts["IT"],    Audience = s,    SortOrder = ++order },
                new AccountType { Code = "AFEFutures",   Name = "AFE Global Futures Back Office System",    ResponsibleDeptId = depts["IT"],     Audience = s,    SortOrder = ++order },
                new AccountType { Code = "SunAccount",   Name = "Sun Account System",                       ResponsibleDeptId = depts["IT"],     Audience = s,    SortOrder = ++order },
                new AccountType { Code = "EReport",      Name = "E-report",                                 ResponsibleDeptId = depts["IT"],     Audience = both, SortOrder = ++order, RequiresDetail = true, DetailLabel = "Access profile" },
                new AccountType { Code = "NetworkLogon", Name = "Network Log on ID",                        ResponsibleDeptId = depts["IT"],     Audience = both, SortOrder = ++order },
                new AccountType { Code = "WebBanking",   Name = "Web Banking System \u2013 HSBC / SCB / BOC (CBS)", ResponsibleDeptId = depts["IT"], Audience = s, SortOrder = ++order },
                new AccountType { Code = "VoiceRecording",Name = "Voice recording",                          ResponsibleDeptId = depts["IT"],     Audience = s,    SortOrder = ++order },
                new AccountType { Code = "ITOthers",     Name = "Others",                                   ResponsibleDeptId = depts["IT"],     Audience = s,    SortOrder = ++order, RequiresDetail = true, DetailLabel = "Specify" },
                new AccountType { Code = "Email",        Name = "E-mail Account",                            ResponsibleDeptId = depts["IT"],     Audience = both, SortOrder = ++order, RequiresDetail = true, DetailLabel = "Group(s) / Sub-group(s)" },

                // --- IT Department (AE/Sales/SA form) ---
                new AccountType { Code = "NextView",     Name = "NextView/Reuters/Bloomberg",               ResponsibleDeptId = depts["IT"],     Audience = ae,   SortOrder = ++order },
                new AccountType { Code = "SharpPoint",   Name = "Sharp Point",                               ResponsibleDeptId = depts["IT"],     Audience = ae,   SortOrder = ++order },
                new AccountType { Code = "ETnetAFE",     Name = "ETnet/AFE/Infocast",                        ResponsibleDeptId = depts["IT"],     Audience = ae,   SortOrder = ++order },
                new AccountType { Code = "Ayers",        Name = "Ayers",                                     ResponsibleDeptId = depts["IT"],     Audience = ae,   SortOrder = ++order },
                new AccountType { Code = "FXES",         Name = "FXES",                                      ResponsibleDeptId = depts["IT"],     Audience = ae,   SortOrder = ++order, RequiresDetail = true, DetailLabel = "Access profile: AE / PWM" },

                // --- HR & Administration Department (both forms) ---
                new AccountType { Code = "Telephone",    Name = "Telephone",                                ResponsibleDeptId = depts["HR"],     Audience = both, SortOrder = ++order, RequiresDetail = true, DetailLabel = "Extension(s) / line details" },
                new AccountType { Code = "NameCard",     Name = "Name Card",                                ResponsibleDeptId = depts["HR"],     Audience = both, SortOrder = ++order },
                new AccountType { Code = "AccessRight4F",Name = "Access Right - 4/F",                        ResponsibleDeptId = depts["HR"],     Audience = both, SortOrder = ++order },
                new AccountType { Code = "AccessRight5F",Name = "Access Right - 5/F",                        ResponsibleDeptId = depts["HR"],     Audience = both, SortOrder = ++order },
                new AccountType { Code = "AccessRight6F",Name = "Access Right - 6/F",                        ResponsibleDeptId = depts["HR"],     Audience = both, SortOrder = ++order },
                new AccountType { Code = "AccessRightRSH",Name = "Access Right - RSH",                       ResponsibleDeptId = depts["HR"],     Audience = both, SortOrder = ++order },
                new AccountType { Code = "AccessRightDLG",Name = "Access Right - DLG",                       ResponsibleDeptId = depts["HR"],     Audience = both, SortOrder = ++order },
                new AccountType { Code = "AccessRightECM",Name = "Access Right - ECM",                       ResponsibleDeptId = depts["HR"],     Audience = both, SortOrder = ++order },
                new AccountType { Code = "SfcLicense",   Name = "SFC License(s)",                            ResponsibleDeptId = depts["HR"],     Audience = both, SortOrder = ++order, RequiresDetail = true, DetailLabel = "License no." });

            await db.SaveChangesAsync();
        }
    }
}

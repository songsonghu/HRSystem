using HRSystem.Domain.Entities;
using HRSystem.Domain.Enums;
using HRSystem.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

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

        // 3b) Corporate/business departments an employee can belong to (as
        // opposed to the account-provisioning departments above). Added
        // individually, if missing by name, so re-running the seeder against
        // an already-seeded database still fills in any newly added entries.
        var corporateDepartmentNames = new[]
        {
            "Private Wealth Mgt",
            "Finance & Accounts",
            "Legal & Compliance",
            "Settlement",
            "Dealing - Global Markets & Structured Products",
            "Sales",
            "Information Technology",
            "Research",
            "Internal Audit",
            "HK Fixed Income & Structured Products",
            "Credit Control",
            "Client Account Services & Middle Office",
            "Equity Capital Market",
            "Insurance",
            "Exec. Office",
            "Central Dealing",
            "Dealing",
            "Marking",
            "E-Business",
            "Administrator",
            "Institutional Sales",
            "Human Resources",
            "HR & Administration",
            "Dealing - Futures"
        };

        var existingDepartmentNames = (await db.Departments.Select(d => d.Name).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newDepartments = corporateDepartmentNames
            .Where(name => !existingDepartmentNames.Contains(name))
            .Select(name => new Department { Name = name })
            .ToList();
        if (newDepartments.Count > 0)
        {
            db.Departments.AddRange(newDepartments);
            await db.SaveChangesAsync();
        }

        await EnsureDepartmentHeadsAsync(db, userManager);

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

                // --- IT Department (shared items, then Staff-only, then AE-only) ---
                // NOTE: the SortOrder values below are chosen so that filtering by
                // audience reproduces each paper form's exact item order, even
                // though "E-report" and "Network Log on ID" swap relative order
                // between the two forms (Staff: E-report then Network Log on ID;
                // AE: Network Log on ID then E-report) - this is why "Network Log
                // on ID" is modeled as two separate rows, one per audience.
                new AccountType { Code = "PC",           Name = "PC",                                       ResponsibleDeptId = depts["IT"],     Audience = both, SortOrder = ++order },
                new AccountType { Code = "Email",        Name = "E-mail Account",                            ResponsibleDeptId = depts["IT"],     Audience = both, SortOrder = ++order, RequiresDetail = true, DetailLabel = "Group(s) / Sub-group(s)" },
                new AccountType { Code = "IBOSystem",    Name = "IBO System (HK/SG/US)",                    ResponsibleDeptId = depts["IT"],     Audience = s,    SortOrder = ++order },
                new AccountType { Code = "AFEEquity",    Name = "AFE Equity Stock Option Back Office System",ResponsibleDeptId = depts["IT"],    Audience = s,    SortOrder = ++order },
                new AccountType { Code = "AFEFutures",   Name = "AFE Global Futures Back Office System",    ResponsibleDeptId = depts["IT"],     Audience = s,    SortOrder = ++order },
                new AccountType { Code = "SunAccount",   Name = "Sun Account System",                       ResponsibleDeptId = depts["IT"],     Audience = s,    SortOrder = ++order },
                new AccountType { Code = "NextView",     Name = "NextView/Reuters/Bloomberg",               ResponsibleDeptId = depts["IT"],     Audience = ae,   SortOrder = ++order },
                new AccountType { Code = "SharpPoint",   Name = "Sharp Point",                               ResponsibleDeptId = depts["IT"],     Audience = ae,   SortOrder = ++order },
                new AccountType { Code = "ETnetAFE",     Name = "ETnet/AFE/Infocast",                        ResponsibleDeptId = depts["IT"],     Audience = ae,   SortOrder = ++order },
                new AccountType { Code = "Ayers",        Name = "Ayers",                                     ResponsibleDeptId = depts["IT"],     Audience = ae,   SortOrder = ++order },
                new AccountType { Code = "NetworkLogonAE",Name = "Network Log on ID",                        ResponsibleDeptId = depts["IT"],     Audience = ae,   SortOrder = ++order },
                new AccountType { Code = "EReport",      Name = "E-report",                                 ResponsibleDeptId = depts["IT"],     Audience = both, SortOrder = ++order, RequiresDetail = true, DetailLabel = "Access profile" },
                new AccountType { Code = "NetworkLogonStaff",Name = "Network Log on ID",                     ResponsibleDeptId = depts["IT"],     Audience = s,    SortOrder = ++order, RequiresDetail = true, DetailLabel = "Access profile" },
                new AccountType { Code = "WebBanking",   Name = "Web Banking System \u2013 HSBC / SCB / BOC (CBS)", ResponsibleDeptId = depts["IT"], Audience = s, SortOrder = ++order },
                new AccountType { Code = "VoiceRecording",Name = "Voice recording",                          ResponsibleDeptId = depts["IT"],     Audience = s,    SortOrder = ++order },
                new AccountType { Code = "ITOthers",     Name = "Others",                                   ResponsibleDeptId = depts["IT"],     Audience = s,    SortOrder = ++order, RequiresDetail = true, DetailLabel = "Specify" },
                new AccountType { Code = "FXES",         Name = "FXES",                                      ResponsibleDeptId = depts["IT"],     Audience = ae,   SortOrder = ++order, RequiresDetail = true, DetailLabel = "Access profile: AE / PWM" },

                // --- HR & Administration Department (both forms) ---
                // Telephone item wording differs slightly between the two paper
                // forms, so it is modeled as two audience-specific rows.
                new AccountType { Code = "TelephoneStaff",Name = "Telephone - 1 internal extension",         ResponsibleDeptId = depts["HR"],     Audience = s,    SortOrder = ++order },
                new AccountType { Code = "TelephoneAE",  Name = "1 / 2 Telephone(s) \u2013 direct line + internal extension", ResponsibleDeptId = depts["HR"], Audience = ae, SortOrder = ++order },
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

        await SeedDepartureTemplatesAsync(db);
    }

    private static async Task EnsureDepartmentHeadsAsync(AppDbContext db, UserManager<ApplicationUser> userManager)
    {
        var seeds = new[]
        {
            new DepartmentHeadSeed("Client Service & Internet Trading", "CSIT", "csit.head@hrsystem.local", "CSIT Department Head"),
            new DepartmentHeadSeed("Credit Department", "CREDIT", "credit.head@hrsystem.local", "Credit Department Head"),
            new DepartmentHeadSeed("IT Department", "IT", "it.head@hrsystem.local", "IT Department Head"),
            new DepartmentHeadSeed("HR & Administration Department", "HR", "hr.head@hrsystem.local", "HR & Administration Head"),
            new DepartmentHeadSeed("Finance & Accounts", null, "finance.departure.head@hrsystem.local", "Finance & Accounts Head"),
            new DepartmentHeadSeed("Credit Control", null, "creditcontrol.departure.head@hrsystem.local", "Credit Control Head"),
            new DepartmentHeadSeed("Legal & Compliance", null, "legal.departure.head@hrsystem.local", "Legal & Compliance Head"),
            new DepartmentHeadSeed("Information Technology", null, "it.departure.head@hrsystem.local", "Information Technology Head"),
            new DepartmentHeadSeed("HR & Administration", null, "hradmin.departure.head@hrsystem.local", "HR & Administration Head")
        };

        foreach (var seed in seeds)
        {
            var department = await db.Departments.FirstOrDefaultAsync(d =>
                (seed.Code != null && d.Code == seed.Code) || d.Name == seed.DepartmentName);
            if (department is null) continue;

            var user = await userManager.FindByEmailAsync(seed.Email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = seed.Email,
                    Email = seed.Email,
                    EmailConfirmed = true,
                    FullName = seed.FullName,
                    DepartmentId = department.Id
                };
                var createResult = await userManager.CreateAsync(user, GenerateSeedPassword());
                if (!createResult.Succeeded) continue;
            }

            if (!await userManager.IsInRoleAsync(user, Roles.DeptHead))
                await userManager.AddToRoleAsync(user, Roles.DeptHead);

            if (department.HeadUserId != user.Id)
            {
                department.HeadUserId = user.Id;
                department.ModifiedAt = DateTime.UtcNow;
                department.ModifiedBy = "system";
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedDepartureTemplatesAsync(AppDbContext db)
    {
        var templates = new[]
        {
            new DepartureTemplateSeed("Finance & Accounts", 1, new[]
            {
                "Delete the login of the following systems: HSBC FX / SCB FX",
                "Check if there is cash advance for opening external broker account / other purpose",
                "Collect e-banking token(s)",
                "Delete user account access to e-banking by admin user or submit deletion form to the banks",
                "Check if there is Commission Rebate / Deficit held up by the Company",
                "Others"
            }),
            new DepartureTemplateSeed("Credit Control", 2, new[]
            {
                "Delete login of Ayers / Sharp Point / 2Go",
                "Update the Company's authorized dealer list with brokers (UOBKH Group and third-party brokers)",
                "Others"
            }),
            new DepartureTemplateSeed("Legal & Compliance", 3, new[]
            {
                "Follow up on outstanding AML items (if any)",
                "Follow up on acknowledgement of Quarterly Newsletters (if any)"
            }),
            new DepartureTemplateSeed("Information Technology", 4, new[]
            {
                "Disable all assigned IT accounts and system access",
                "Collect and verify return of IT assets/equipment",
                "Revoke network/VPN and email access",
                "Others"
            }),
            new DepartureTemplateSeed("HR & Administration", 5, new[]
            {
                "Update Staff Movement Checklist and HR system",
                "Calculate final payment and confirm with leaving staff",
                "Terminate Medical Plan, Work Permit, and MPF",
                "Prepare IR56F / IR56G and de-register SFC license as applicable",
                "Send resignation acknowledgement / departure notifications and conduct exit interview",
                "Collect staff access card / keys / equipment / e-banking token(s)",
                "Release final payment within 7 days from the last employment date",
                "Update photo album and intranet directory",
                "Others"
            })
        };

        foreach (var templateSeed in templates)
        {
            var template = await db.DepartureTaskTemplates
                .Include(t => t.Items)
                .FirstOrDefaultAsync(t => t.DepartmentName == templateSeed.DepartmentName);

            if (template is null)
            {
                template = new DepartureTaskTemplate
                {
                    DepartmentName = templateSeed.DepartmentName,
                    SortOrder = templateSeed.SortOrder,
                    IsActive = true,
                    IsRequired = true,
                    CreatedBy = "system"
                };
                db.DepartureTaskTemplates.Add(template);
            }
            else
            {
                template.SortOrder = templateSeed.SortOrder;
                template.IsActive = true;
                template.IsRequired = true;
                template.ModifiedAt = DateTime.UtcNow;
                template.ModifiedBy = "system";
            }

            var existingItems = template.Items.ToDictionary(i => i.SortOrder);
            for (int index = 0; index < templateSeed.Items.Length; index++)
            {
                var sortOrder = index + 1;
                if (existingItems.TryGetValue(sortOrder, out var existing))
                {
                    existing.Description = templateSeed.Items[index];
                    existing.IsRequired = true;
                    existing.ModifiedAt = DateTime.UtcNow;
                    existing.ModifiedBy = "system";
                    continue;
                }

                template.Items.Add(new DepartureTaskTemplateItem
                {
                    Description = templateSeed.Items[index],
                    SortOrder = sortOrder,
                    IsRequired = true,
                    CreatedBy = "system"
                });
            }
        }

        await db.SaveChangesAsync();
    }

    private sealed record DepartmentHeadSeed(string DepartmentName, string? Code, string Email, string FullName);
    private sealed record DepartureTemplateSeed(string DepartmentName, int SortOrder, string[] Items);

    private static string GenerateSeedPassword()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        return $"Dh!{token}a9";
    }
}

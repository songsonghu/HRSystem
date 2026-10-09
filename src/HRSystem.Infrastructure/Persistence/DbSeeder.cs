using System.Security.Claims;
using HRSystem.Application.Security;
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
    // Keep in sync with the migrations that grant the same permissions to existing databases
    // (AddEmployeeUserLinkAndPermissions, AddDepartmentsAndGender, AddOnboardingChecklists).
    private static readonly (string Role, string[] Permissions)[] DefaultRolePermissions =
    {
        (Roles.HR, new[]
        {
            Permissions.EmployeesManage, Permissions.DepartmentsManage, Permissions.AccountRequestsManage,
            Permissions.OnboardingManage,
            Permissions.DeparturesManage, Permissions.OffboardingExport
        })
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<AppDbContext>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        await db.Database.MigrateAsync();

        // Roles and users are managed in the UI after installation, so defaults are only
        // created on a fresh database; otherwise deleted roles/users would reappear on restart.
        bool freshInstall = !await roleManager.Roles.AnyAsync();

        // 1) Roles. Admin is built in and implicitly holds every permission.
        if (!await roleManager.RoleExistsAsync(Roles.Admin))
            await roleManager.CreateAsync(new IdentityRole(Roles.Admin));

        if (freshInstall)
        {
            foreach (var (roleName, permissions) in DefaultRolePermissions)
            {
                var role = new IdentityRole(roleName);
                await roleManager.CreateAsync(role);
                foreach (var permission in permissions)
                    await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission));
            }
        }

        // 2) Default admin account, only while no Admin user exists.
        const string adminEmail = "admin@hrsystem.local";
        if ((await userManager.GetUsersInRoleAsync(Roles.Admin)).Count == 0
            && await userManager.FindByEmailAsync(adminEmail) is null)
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

        // 3) Departments that own account types, mirroring the sections printed on the two
        // paper requisition forms. Managers are assigned in the UI (Departments page).
        if (!await db.Departments.AnyAsync())
        {
            db.Departments.AddRange(
                new Department { Name = "Client Service & Internet Trading", Code = "CSIT" },
                new Department { Name = "Credit Department",                 Code = "CREDIT" },
                new Department { Name = "IT Department",                     Code = "IT" },
                new Department { Name = "HR & Administration Department",    Code = "HR" });
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

        // 4) Account types mapped to responsible departments, tagged with the
        // requisition form(s) they appear on (Staff / AE / Both).
        if (!await db.AccountTypes.AnyAsync())
        {
            // Only the account-owning departments have codes; the corporate ones above do not.
            var depts = await db.Departments.Where(d => d.Code != null).ToDictionaryAsync(d => d.Code!, d => d.Id);
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

        await SeedChecklistTemplatesAsync(db, ChecklistKind.Departure, DepartureTemplates);
        await SeedChecklistTemplatesAsync(db, ChecklistKind.Onboarding, OnboardingTemplates);
    }

    private static readonly TemplateSeed[] DepartureTemplates =
    {
        new("Finance & Accounts", new[]
        {
            "Delete the login of the following systems: HSBC FX / SCB FX",
            "Check if there is cash advance for opening external broker account / other purpose",
            "Collect e-banking token(s)",
            "Delete user account access to e-banking by admin user or submit deletion form to the banks",
            "Check if there is Commission Rebate / Deficit held up by the Company",
            "Others"
        }),
        new("Credit Control", new[]
        {
            "Delete login of Ayers / Sharp Point / 2Go",
            "Update the Company's authorized dealer list with brokers (UOBKH Group and third-party brokers)",
            "Others"
        }),
        new("Legal & Compliance", new[]
        {
            "Follow up on outstanding AML items (if any)",
            "Follow up on acknowledgement of Quarterly Newsletters (if any)"
        }),
        new("Information Technology", new[]
        {
            "Disable all assigned IT accounts and system access",
            "Collect and verify return of IT assets/equipment",
            "Revoke network/VPN and email access",
            "Others"
        }),
        new("HR & Administration", new[]
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

    private static readonly TemplateSeed[] OnboardingTemplates =
    {
        new("HR & Administration", new[]
        {
            "Sign employment contract and collect documents (ID, address proof, certificates)",
            "Enrol in MPF and medical insurance",
            "Arrange seat and issue staff access card / keys",
            "Orientation and staff handbook",
            "Update HR system and intranet directory",
            "Others"
        }, OptionalOthers: true),
        new("Information Technology", new[]
        {
            "Prepare PC and peripherals",
            "Set up telephone extension",
            "IT security briefing",
            "Others"
        }, OptionalOthers: true),
        new("Finance & Accounts", new[]
        {
            "Set up payroll and bank account details",
            "Others"
        }, OptionalOthers: true),
        new("Legal & Compliance", new[]
        {
            "Compliance and AML training",
            "Personal account dealing declaration",
            "SFC licence registration / transfer (if applicable)",
            "Code of conduct acknowledgement"
        }, OptionalOthers: true)
    };

    /// <summary>
    /// Seeds a kind's templates only while it has none, so edits made on the Checklist
    /// Templates page are never overwritten. Departments missing by name are skipped.
    /// </summary>
    private static async Task SeedChecklistTemplatesAsync(AppDbContext db, ChecklistKind kind, IReadOnlyList<TemplateSeed> seeds)
    {
        if (await db.ChecklistTemplates.AnyAsync(t => t.Kind == kind)) return;

        var departments = await db.Departments.ToDictionaryAsync(d => d.Name, d => d.Id, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < seeds.Count; i++)
        {
            var seed = seeds[i];
            if (!departments.TryGetValue(seed.DepartmentName, out var departmentId)) continue;

            var template = new ChecklistTemplate
            {
                Kind = kind,
                DepartmentId = departmentId,
                SortOrder = i + 1,
                IsActive = true,
                IsRequired = true,
                CreatedBy = "system"
            };
            for (int j = 0; j < seed.Items.Length; j++)
            {
                template.Items.Add(new ChecklistTemplateItem
                {
                    Description = seed.Items[j],
                    SortOrder = j + 1,
                    IsRequired = !(seed.OptionalOthers && seed.Items[j] == "Others"),
                    CreatedBy = "system"
                });
            }
            db.ChecklistTemplates.Add(template);
        }

        await db.SaveChangesAsync();
    }

    private sealed record TemplateSeed(string DepartmentName, string[] Items, bool OptionalOthers = false);
}

using HRSystem.Application.Interfaces;
using HRSystem.Infrastructure.Identity;
using HRSystem.Infrastructure.Persistence;
using HRSystem.Infrastructure.Services;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRSystem.Infrastructure;

/// <summary>Registers Infrastructure-layer services (EF Core, Identity, Hangfire, etc.).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        // EF Core + SQL Server
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

        // Expose the concrete context through the Application abstraction.
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // ASP.NET Core Identity (users, roles, password policy).
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        // Point the Identity cookie at the actual Razor Page routes under the
        // "Identity" area (the default paths "/Account/Login" etc. do not
        // exist and would otherwise result in a 404).
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Identity/Account/Login";
            options.LogoutPath = "/Identity/Account/Logout";
            options.AccessDeniedPath = "/Identity/Account/AccessDenied";
        });

        // Bind options
        services.Configure<SmtpOptions>(config.GetSection("Smtp"));
        services.Configure<FileStorageOptions>(config.GetSection("FileStorage"));

        // Hangfire background jobs (email queue, retries).
        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                SchemaName = "HangFire",
                QueuePollInterval = TimeSpan.FromSeconds(15)
            }));
        services.AddHangfireServer();

        // Infrastructure service implementations
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IUserDirectoryService, UserDirectoryService>();

        return services;
    }
}

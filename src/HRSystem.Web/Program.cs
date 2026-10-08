using FluentValidation.AspNetCore;
using HRSystem.Application;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Infrastructure;
using HRSystem.Infrastructure.Identity;
using HRSystem.Infrastructure.Persistence;
using HRSystem.Web.Services;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Serilog;
using Hangfire.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// --- Serilog structured logging ---
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day));

// --- MVC + Razor + FluentValidation ---
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(); // for Identity UI
builder.Services.AddFluentValidationAutoValidation()
    .AddFluentValidationClientsideAdapters();

// --- HttpContext + current user bridge ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>(); 

// --- Application & Infrastructure layers ---
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// --- Authorization: one policy per permission, named after the permission ---
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All)
        options.AddPolicy(permission.Name, p => p.RequireAssertion(ctx => ctx.User.HasPermission(permission.Name)));
});

var app = builder.Build();

// --- Middleware pipeline ---
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();    // ← 必须在前:识别"你是谁"
app.UseAuthorization();     // ← 再判断"你能不能进"

// Hangfire dashboard (admin only).
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAdminAuthorizationFilter() }
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// --- Apply migrations & seed baseline data on startup ---
await DbSeeder.SeedAsync(app.Services);

app.Run();

/// <summary>Restricts the Hangfire dashboard to users with the system.jobs permission.</summary>
public class HangfireAdminAuthorizationFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true
               && httpContext.User.HasPermission(Permissions.SystemJobs);
    }
}

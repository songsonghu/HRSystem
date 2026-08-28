using HRSystem.Application.Interfaces;
using HRSystem.Domain.Entities;
using HRSystem.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Infrastructure.Persistence;

/// <summary>
/// EF Core database context. Extends the Identity context so users, roles and
/// business tables share a single database, and implements
/// <see cref="IAppDbContext"/> for the Application layer.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<AccountType> AccountTypes => Set<AccountType>();
    public DbSet<AccountRequest> AccountRequests => Set<AccountRequest>();
    public DbSet<AccountRequestItem> AccountRequestItems => Set<AccountRequestItem>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<EmployeeAccount> EmployeeAccounts => Set<EmployeeAccount>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Apply all IEntityTypeConfiguration<T> in this assembly.
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

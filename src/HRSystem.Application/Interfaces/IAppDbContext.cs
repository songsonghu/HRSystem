using HRSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Interfaces;

/// <summary>
/// Abstraction over the EF Core DbContext so the Application layer stays free
/// of the concrete Infrastructure implementation.
/// </summary>
public interface IAppDbContext
{
    DbSet<Employee> Employees { get; }
    DbSet<Department> Departments { get; }
    DbSet<AccountType> AccountTypes { get; }
    DbSet<AccountRequest> AccountRequests { get; }
    DbSet<AccountRequestItem> AccountRequestItems { get; }
    DbSet<Attachment> Attachments { get; }
    DbSet<EmployeeAttachment> EmployeeAttachments { get; }
    DbSet<EmployeeAccount> EmployeeAccounts { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<DepartureRequest> DepartureRequests { get; }
    DbSet<DepartureTask> DepartureTasks { get; }
    DbSet<DepartureTaskItem> DepartureTaskItems { get; }
    DbSet<DepartureTaskTemplate> DepartureTaskTemplates { get; }
    DbSet<DepartureTaskTemplateItem> DepartureTaskTemplateItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

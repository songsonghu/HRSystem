using HRSystem.Domain.Entities;

namespace HRSystem.Application.Interfaces;

/// <summary>
/// Unit of Work pattern that coordinates repository operations and manages transactions.
/// Ensures data consistency and provides a single SaveChanges operation.
/// Note: AuditLog is not included as it uses long ID; access it directly via IAppDbContext.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    IRepository<Employee> Employees { get; }
    IRepository<Department> Departments { get; }
    IRepository<AccountType> AccountTypes { get; }
    IRepository<AccountRequest> AccountRequests { get; }
    IRepository<AccountRequestItem> AccountRequestItems { get; }
    IRepository<Attachment> Attachments { get; }
    IRepository<EmployeeAttachment> EmployeeAttachments { get; }
    IRepository<EmployeeAccount> EmployeeAccounts { get; }
    IRepository<DepartureRequest> DepartureRequests { get; }
    IRepository<DepartureTask> DepartureTasks { get; }
    IRepository<DepartureTaskItem> DepartureTaskItems { get; }
    IRepository<DepartureTaskTemplate> DepartureTaskTemplates { get; }
    IRepository<DepartureTaskTemplateItem> DepartureTaskTemplateItems { get; }

    /// <summary>Save all pending changes to the database.</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Begin a database transaction.</summary>
    Task BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>Commit the current transaction.</summary>
    Task CommitTransactionAsync(CancellationToken ct = default);

    /// <summary>Rollback the current transaction.</summary>
    Task RollbackTransactionAsync(CancellationToken ct = default);
}

using HRSystem.Application.Interfaces;
using HRSystem.Domain.Entities;
using Microsoft.EntityFrameworkCore.Storage;

namespace HRSystem.Infrastructure.Persistence;

/// <summary>
/// Unit of Work implementation that manages repository lifecycle and transactions.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private IDbContextTransaction? _transaction;

    // Lazy-loaded repositories
    private IRepository<Employee>? _employees;
    private IRepository<Department>? _departments;
    private IRepository<AccountType>? _accountTypes;
    private IRepository<AccountRequest>? _accountRequests;
    private IRepository<AccountRequestItem>? _accountRequestItems;
    private IRepository<Attachment>? _attachments;
    private IRepository<EmployeeAttachment>? _employeeAttachments;
    private IRepository<EmployeeAccount>? _employeeAccounts;
    private IRepository<DepartureRequest>? _departureRequests;
    private IRepository<DepartureTask>? _departureTasks;
    private IRepository<DepartureTaskItem>? _departureTaskItems;
    private IRepository<DepartureTaskTemplate>? _departureTaskTemplates;
    private IRepository<DepartureTaskTemplateItem>? _departureTaskTemplateItems;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public IRepository<Employee> Employees
        => _employees ??= new Repository<Employee>(_context);

    public IRepository<Department> Departments
        => _departments ??= new Repository<Department>(_context);

    public IRepository<AccountType> AccountTypes
        => _accountTypes ??= new Repository<AccountType>(_context);

    public IRepository<AccountRequest> AccountRequests
        => _accountRequests ??= new Repository<AccountRequest>(_context);

    public IRepository<AccountRequestItem> AccountRequestItems
        => _accountRequestItems ??= new Repository<AccountRequestItem>(_context);

    public IRepository<Attachment> Attachments
        => _attachments ??= new Repository<Attachment>(_context);

    public IRepository<EmployeeAttachment> EmployeeAttachments
        => _employeeAttachments ??= new Repository<EmployeeAttachment>(_context);

    public IRepository<EmployeeAccount> EmployeeAccounts
        => _employeeAccounts ??= new Repository<EmployeeAccount>(_context);

    public IRepository<DepartureRequest> DepartureRequests
        => _departureRequests ??= new Repository<DepartureRequest>(_context);

    public IRepository<DepartureTask> DepartureTasks
        => _departureTasks ??= new Repository<DepartureTask>(_context);

    public IRepository<DepartureTaskItem> DepartureTaskItems
        => _departureTaskItems ??= new Repository<DepartureTaskItem>(_context);

    public IRepository<DepartureTaskTemplate> DepartureTaskTemplates
        => _departureTaskTemplates ??= new Repository<DepartureTaskTemplate>(_context);

    public IRepository<DepartureTaskTemplateItem> DepartureTaskTemplateItems
        => _departureTaskTemplateItems ??= new Repository<DepartureTaskTemplateItem>(_context);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        _transaction = await _context.Database.BeginTransactionAsync(ct);
    }

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        try
        {
            await SaveChangesAsync(ct);
            if (_transaction != null)
                await _transaction.CommitAsync(ct);
        }
        catch
        {
            await RollbackTransactionAsync(ct);
            throw;
        }
        finally
        {
            if (_transaction != null)
                await _transaction.DisposeAsync();
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        try
        {
            if (_transaction != null)
                await _transaction.RollbackAsync(ct);
        }
        finally
        {
            if (_transaction != null)
                await _transaction.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction != null)
            await _transaction.DisposeAsync();
        await _context.DisposeAsync();
    }
}

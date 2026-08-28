using HRSystem.Application.Interfaces;
using HRSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Services;

/// <summary>
/// Produces the offboarding account listing and its Excel export (Module 5).
/// </summary>
public class OffboardingService : IOffboardingService
{
    private readonly IAppDbContext _db;
    private readonly IReportService _report;
    private readonly IAuditService _audit;

    public OffboardingService(IAppDbContext db, IReportService report, IAuditService audit)
    {
        _db = db;
        _report = report;
        _audit = audit;
    }

    public async Task<IReadOnlyList<OffboardAccountRow>> GetActiveAccountsAsync(int employeeId, CancellationToken ct = default)
    {
        return await _db.EmployeeAccounts.AsNoTracking()
            .Include(a => a.AccountType)
            .Where(a => a.EmployeeId == employeeId && a.Status == AccountStatus.Active)
            .OrderBy(a => a.AccountType!.SortOrder)
            .Select(a => new OffboardAccountRow(
                a.AccountType!.Name,
                a.AccountValue,
                a.Status.ToString(),
                a.OpenedAt))
            .ToListAsync(ct);
    }

    public async Task<byte[]> ExportExcelAsync(int employeeId, CancellationToken ct = default)
    {
        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct)
            ?? throw new InvalidOperationException("Employee not found.");

        var rows = await GetActiveAccountsAsync(employeeId, ct);
        var bytes = _report.BuildOffboardingWorkbook(employee.EmployeeNo, employee.Name, rows);

        await _audit.LogAsync("ExportOffboarding", nameof(Domain.Entities.Employee),
            employeeId.ToString(), $"{rows.Count} accounts", ct);
        return bytes;
    }
}

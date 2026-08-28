using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Domain.Entities;
using HRSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Services;

/// <summary>Implements employee CRUD with soft-delete and audit (Module 2).</summary>
public class EmployeeService : IEmployeeService
{
    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;

    public EmployeeService(IAppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<EmployeeDto>> GetListAsync(string? keyword = null, CancellationToken ct = default)
    {
        var query = _db.Employees.AsNoTracking().Where(e => !e.IsDeleted);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            keyword = keyword.Trim();
            query = query.Where(e =>
                e.Name.Contains(keyword) ||
                e.EmployeeNo.Contains(keyword) ||
                (e.Department != null && e.Department.Contains(keyword)));
        }

        return await query
            .OrderByDescending(e => e.Id)
            .Select(e => Map(e))
            .ToListAsync(ct);
    }

    public async Task<EmployeeDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var e = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        return e is null ? null : Map(e);
    }

    public async Task<Result<int>> CreateAsync(EmployeeEditDto dto, CancellationToken ct = default)
    {
        bool exists = await _db.Employees.AnyAsync(e => e.EmployeeNo == dto.EmployeeNo && !e.IsDeleted, ct);
        if (exists) return Result<int>.Fail($"Employee number '{dto.EmployeeNo}' already exists.");

        var entity = new Employee
        {
            EmployeeNo = dto.EmployeeNo,
            Name = dto.Name,
            Email = dto.Email,
            Department = dto.Department,
            Position = dto.Position,
            JoinDate = dto.JoinDate,
            ResignDate = dto.ResignDate,
            Status = dto.Status
        };

        _db.Employees.Add(entity);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("CreateEmployee", nameof(Employee), entity.Id.ToString(), entity.EmployeeNo, ct);
        return Result<int>.Success(entity.Id);
    }

    public async Task<Result> UpdateAsync(EmployeeEditDto dto, CancellationToken ct = default)
    {
        var entity = await _db.Employees.FirstOrDefaultAsync(e => e.Id == dto.Id && !e.IsDeleted, ct);
        if (entity is null) return Result.Fail("Employee not found.");

        entity.Name = dto.Name;
        entity.Email = dto.Email;
        entity.Department = dto.Department;
        entity.Position = dto.Position;
        entity.JoinDate = dto.JoinDate;
        entity.ResignDate = dto.ResignDate;
        entity.Status = dto.Status;

        // Stamp resignation date automatically when moving to Resigned.
        if (dto.Status == EmployeeStatus.Resigned && entity.ResignDate is null)
            entity.ResignDate = DateTime.Today;

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("UpdateEmployee", nameof(Employee), entity.Id.ToString(), null, ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted, ct);
        if (entity is null) return Result.Fail("Employee not found.");

        entity.IsDeleted = true; // soft delete keeps the audit trail intact
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("DeleteEmployee", nameof(Employee), entity.Id.ToString(), null, ct);
        return Result.Success();
    }

    private static EmployeeDto Map(Employee e) => new()
    {
        Id = e.Id,
        EmployeeNo = e.EmployeeNo,
        Name = e.Name,
        Email = e.Email,
        Department = e.Department,
        Position = e.Position,
        JoinDate = e.JoinDate,
        ResignDate = e.ResignDate,
        Status = e.Status
    };
}

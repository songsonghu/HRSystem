using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Domain.Entities;
using HRSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Services;

public class DepartmentService : IDepartmentService
{
    private readonly IAppDbContext _db;
    private readonly IAuditService _audit;

    public DepartmentService(IAppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<DepartmentListItemDto>> GetListAsync(CancellationToken ct = default)
    {
        return await _db.Departments.AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentListItemDto
            {
                Id = d.Id,
                Name = d.Name,
                Code = d.Code,
                IsActive = d.IsActive,
                ManagerName = d.Manager != null ? d.Manager.Name : null,
                ManagerCanReceiveTasks = d.Manager != null && d.Manager.UserId != null,
                EmployeeCount = d.Employees.Count(e => !e.IsDeleted)
            })
            .ToListAsync(ct);
    }

    public async Task<DepartmentDetailsDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var d = await _db.Departments.AsNoTracking()
            .Include(x => x.Manager)
            .Include(x => x.Employees)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return null;

        return new DepartmentDetailsDto
        {
            Id = d.Id,
            Name = d.Name,
            Code = d.Code,
            IsActive = d.IsActive,
            ManagerEmployeeId = d.ManagerEmployeeId,
            ManagerName = d.Manager?.Name,
            ManagerCanReceiveTasks = d.Manager?.UserId != null,
            EmployeeCount = d.Employees.Count,
            Employees = d.Employees
                .OrderBy(e => e.Status).ThenBy(e => e.Name)
                .Select(e => new DepartmentMemberDto
                {
                    Id = e.Id,
                    EmployeeNo = e.EmployeeNo,
                    Name = e.Name,
                    Position = e.Position,
                    Status = e.Status,
                    HasLogin = e.UserId != null
                })
                .ToList()
        };
    }

    public async Task<IReadOnlyList<EmployeeOptionDto>> GetManagerOptionsAsync(CancellationToken ct = default)
    {
        return await _db.Employees.AsNoTracking()
            .Where(e => e.Status == EmployeeStatus.Active)
            .OrderBy(e => e.Name)
            .Select(e => new EmployeeOptionDto(e.Id,
                e.Name + " (" + e.EmployeeNo + ")"
                + (e.Department != null ? " – " + e.Department.Name : "")
                + (e.UserId == null ? " [no login]" : "")))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DepartmentOptionDto>> GetOptionsAsync(int? includeId = null, CancellationToken ct = default)
    {
        return await _db.Departments.AsNoTracking()
            .Where(d => d.IsActive || d.Id == includeId)
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentOptionDto(d.Id, d.Name))
            .ToListAsync(ct);
    }

    public async Task<Result<int>> CreateAsync(DepartmentEditDto dto, CancellationToken ct = default)
    {
        var name = dto.Name.Trim();
        if (await _db.Departments.AnyAsync(d => d.Name == name, ct))
            return Result<int>.Fail($"Department '{name}' already exists.");

        var managerError = await ValidateManagerAsync(dto.ManagerEmployeeId, ct);
        if (managerError is not null) return Result<int>.Fail(managerError);

        var entity = new Department
        {
            Name = name,
            Code = string.IsNullOrWhiteSpace(dto.Code) ? null : dto.Code.Trim(),
            ManagerEmployeeId = dto.ManagerEmployeeId,
            IsActive = dto.IsActive
        };
        _db.Departments.Add(entity);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("CreateDepartment", nameof(Department), entity.Id.ToString(), name, ct);
        return Result<int>.Success(entity.Id);
    }

    public async Task<Result> UpdateAsync(DepartmentEditDto dto, CancellationToken ct = default)
    {
        var entity = await _db.Departments.FirstOrDefaultAsync(d => d.Id == dto.Id, ct);
        if (entity is null) return Result.Fail("Department not found.");

        var name = dto.Name.Trim();
        if (name != entity.Name && await _db.Departments.AnyAsync(d => d.Name == name && d.Id != dto.Id, ct))
            return Result.Fail($"Department '{name}' already exists.");

        var managerError = await ValidateManagerAsync(dto.ManagerEmployeeId, ct);
        if (managerError is not null) return Result.Fail(managerError);

        entity.Name = name;
        entity.Code = string.IsNullOrWhiteSpace(dto.Code) ? null : dto.Code.Trim();
        entity.ManagerEmployeeId = dto.ManagerEmployeeId;
        entity.IsActive = dto.IsActive;
        entity.ModifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("UpdateDepartment", nameof(Department), entity.Id.ToString(),
            $"{name}; manager={dto.ManagerEmployeeId}; active={dto.IsActive}", ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (entity is null) return Result.Fail("Department not found.");

        var usages = new List<string>();
        if (await _db.Employees.IgnoreQueryFilters().AnyAsync(e => e.DepartmentId == id, ct)) usages.Add("employees");
        if (await _db.AccountTypes.AnyAsync(a => a.ResponsibleDeptId == id, ct)) usages.Add("account types");
        if (await _db.AccountRequestItems.AnyAsync(i => i.AssignedDeptId == id, ct)) usages.Add("account requests");
        if (await _db.ChecklistTasks.AnyAsync(t => t.AssignedDepartmentId == id, ct)) usages.Add("onboarding/departure tasks");
        if (await _db.ChecklistTemplates.AnyAsync(t => t.DepartmentId == id && !t.IsDeleted, ct)) usages.Add("checklist templates");
        if (usages.Count > 0)
            return Result.Fail($"This department is used by {string.Join(", ", usages)}. Deactivate it instead.");

        _db.Departments.Remove(entity);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("DeleteDepartment", nameof(Department), id.ToString(), entity.Name, ct);
        return Result.Success();
    }

    private async Task<string?> ValidateManagerAsync(int? employeeId, CancellationToken ct)
    {
        if (employeeId is null) return null;
        var manager = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (manager is null) return "Selected manager not found.";
        return manager.Status == EmployeeStatus.Active ? null : $"{manager.Name} is not an active employee.";
    }
}

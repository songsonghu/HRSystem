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
    private readonly IFileStorageService _files;
    private readonly ICurrentUser _currentUser;

    public EmployeeService(IAppDbContext db, IAuditService audit, IFileStorageService files, ICurrentUser currentUser)
    {
        _db = db;
        _audit = audit;
        _files = files;
        _currentUser = currentUser;
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
            .Include(x => x.Attachments)
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
            Status = dto.Status,
            Category = dto.Category
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
        entity.Category = dto.Category;

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

    public async Task<Result> AddAttachmentAsync(
        int employeeId, Stream content, string fileName, string? contentType, CancellationToken ct = default)
    {
        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.Id == employeeId && !e.IsDeleted, ct);
        if (employee is null) return Result.Fail("Employee not found.");

        var stored = await _files.SaveAsync(content, fileName, contentType, ct);
        _db.EmployeeAttachments.Add(new EmployeeAttachment
        {
            EmployeeId = employeeId,
            FileName = stored.FileName,
            FilePath = stored.RelativePath,
            ContentType = stored.ContentType,
            FileSize = stored.Size,
            CreatedBy = _currentUser.UserId
        });

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("AddEmployeeAttachment", nameof(Employee), employeeId.ToString(), stored.FileName, ct);
        return Result.Success();
    }

    public async Task<Result> ReplaceAttachmentsAsync(
        int employeeId, IReadOnlyList<EmployeeAttachmentUpload> attachments, CancellationToken ct = default)
    {
        var employee = await _db.Employees
            .Include(e => e.Attachments)
            .FirstOrDefaultAsync(e => e.Id == employeeId && !e.IsDeleted, ct);
        if (employee is null) return Result.Fail("Employee not found.");

        var storedFiles = new List<StoredFile>();
        try
        {
            foreach (var attachment in attachments)
                storedFiles.Add(await _files.SaveAsync(attachment.Content, attachment.FileName, attachment.ContentType, ct));
        }
        catch
        {
            foreach (var stored in storedFiles)
                await _files.DeleteAsync(stored.RelativePath, ct);
            throw;
        }

        var existingAttachments = employee.Attachments.ToList();
        _db.EmployeeAttachments.RemoveRange(existingAttachments);
        foreach (var stored in storedFiles)
        {
            _db.EmployeeAttachments.Add(new EmployeeAttachment
            {
                EmployeeId = employeeId,
                FileName = stored.FileName,
                FilePath = stored.RelativePath,
                ContentType = stored.ContentType,
                FileSize = stored.Size,
                CreatedBy = _currentUser.UserId
            });
        }

        await _db.SaveChangesAsync(ct);
        foreach (var existing in existingAttachments)
            await _files.DeleteAsync(existing.FilePath, ct);

        await _audit.LogAsync("ReplaceEmployeeAttachments", nameof(Employee), employeeId.ToString(), null, ct);
        return Result.Success();
    }

    public async Task<EmployeeAttachmentFile?> OpenAttachmentAsync(
        int employeeId, int attachmentId, CancellationToken ct = default)
    {
        var attachment = await _db.EmployeeAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.EmployeeId == employeeId, ct);
        if (attachment is null) return null;

        var content = await _files.OpenReadAsync(attachment.FilePath, ct);
        return new EmployeeAttachmentFile(content, attachment.FileName, attachment.ContentType);
    }

    public async Task<IReadOnlyList<string>> GetDepartmentNamesAsync(CancellationToken ct = default)
    {
        return await _db.Departments.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => d.Name)
            .ToListAsync(ct);
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
        Status = e.Status,
        Category = e.Category,
        Attachments = e.Attachments
            .OrderByDescending(a => a.Id)
            .Select(a => new EmployeeAttachmentDto
            {
                Id = a.Id,
                FileName = a.FileName,
                ContentType = a.ContentType,
                FileSize = a.FileSize
            })
            .ToList()
    };
}

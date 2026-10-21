using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Domain.Entities;
using HRSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Services;

/// <summary>
/// Department checklist templates. Submitted requests copy the items, so templates can be
/// edited or deleted without affecting work in progress.
/// </summary>
public class ChecklistTemplateService : IChecklistTemplateService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IUserDirectoryService _users;

    public ChecklistTemplateService(IAppDbContext db, ICurrentUser currentUser, IAuditService audit, IUserDirectoryService users)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _users = users;
    }

    private bool CanManage(ChecklistKind kind) => _currentUser.HasPermission(ChecklistService.ManagePermission(kind));

    public async Task<IReadOnlyList<ChecklistTemplateListItemDto>> GetListAsync(ChecklistKind kind, CancellationToken ct = default)
    {
        if (!CanManage(kind)) return Array.Empty<ChecklistTemplateListItemDto>();

        var templates = await _db.ChecklistTemplates.AsNoTracking()
            .Where(t => t.Kind == kind && !t.IsDeleted)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Department!.Name)
            .Select(t => new ChecklistTemplateListItemDto
            {
                Id = t.Id,
                Kind = t.Kind,
                DepartmentName = t.Department!.Name,
                IsActive = t.IsActive,
                IsRequired = t.IsRequired,
                SortOrder = t.SortOrder,
                ItemCount = t.Items.Count(i => !i.IsDeleted),
                AssigneeUserId = t.ApproverUserId ?? t.Department.Manager!.UserId
            })
            .ToListAsync(ct);

        var names = await _users.GetByIdsAsync(templates.Where(t => t.AssigneeUserId != null).Select(t => t.AssigneeUserId!), ct);
        foreach (var t in templates)
        {
            t.AssigneeName = t.AssigneeUserId is not null && names.TryGetValue(t.AssigneeUserId, out var user) ? user.Display : null;
            t.CanReceiveTasks = t.AssigneeName is not null;
        }
        return templates;
    }

    // -------------------------------------------------------------- approvers

    public async Task<IReadOnlyList<ChecklistApproverDto>> GetApproversAsync(ChecklistKind kind, CancellationToken ct = default)
    {
        if (!CanManage(kind)) return Array.Empty<ChecklistApproverDto>();

        var rows = await _db.ChecklistTemplates.AsNoTracking()
            .Where(t => t.Kind == kind && !t.IsDeleted)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Department!.Name)
            .Select(t => new
            {
                Dto = new ChecklistApproverDto
                {
                    TemplateId = t.Id,
                    DepartmentName = t.Department!.Name,
                    ItemCount = t.Items.Count(i => !i.IsDeleted),
                    IsActive = t.IsActive,
                    ApproverUserId = t.ApproverUserId
                },
                ManagerUserId = t.Department.Manager!.UserId
            })
            .ToListAsync(ct);

        var managers = await _users.GetByIdsAsync(rows.Where(r => r.ManagerUserId != null).Select(r => r.ManagerUserId!), ct);
        foreach (var row in rows)
            row.Dto.ManagerUserName = row.ManagerUserId is not null && managers.TryGetValue(row.ManagerUserId, out var m) ? m.Display : null;
        return rows.Select(r => r.Dto).ToList();
    }

    public async Task<Result> SetApproversAsync(ChecklistKind kind, IReadOnlyDictionary<int, string?> approvers, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(kind) || !CanManage(kind)) return Result.Fail("You do not have permission to manage these approvers.");

        var wanted = approvers.ToDictionary(a => a.Key, a => string.IsNullOrWhiteSpace(a.Value) ? null : a.Value.Trim());
        var users = await _users.GetByIdsAsync(wanted.Values.Where(v => v is not null).Select(v => v!), ct);
        var unknown = wanted.Values.FirstOrDefault(v => v is not null && !users.ContainsKey(v));
        if (unknown is not null) return Result.Fail("A selected approver account no longer exists.");
        var noEmail = users.Values.FirstOrDefault(u => string.IsNullOrWhiteSpace(u.Email));
        if (noEmail is not null) return Result.Fail($"{noEmail.Display} has no email address, so it cannot receive tasks.");

        var templates = await _db.ChecklistTemplates
            .Include(t => t.Department).ThenInclude(d => d!.Manager)
            .Where(t => t.Kind == kind && !t.IsDeleted && wanted.Keys.Contains(t.Id))
            .ToListAsync(ct);

        var changed = new List<string>();
        foreach (var template in templates)
        {
            var approverId = wanted[template.Id];
            if (approverId == template.ApproverUserId) continue;

            template.ApproverUserId = approverId;
            template.ModifiedBy = _currentUser.UserId;
            template.ModifiedAt = DateTime.UtcNow;
            changed.Add($"{template.Department?.Name}={approverId ?? "(manager)"}");

            // Hand over the department's unfinished tasks so the new approver sees them in My Checklist Tasks.
            var assigneeId = approverId ?? template.Department?.Manager?.UserId;
            if (assigneeId is null) continue;
            var assignee = users.TryGetValue(assigneeId, out var known) ? known : await _users.GetByIdAsync(assigneeId, ct);
            var openTasks = await _db.ChecklistTasks
                .Where(t => t.AssignedDepartmentId == template.DepartmentId
                            && t.ChecklistRequest!.Kind == kind
                            && t.ChecklistRequest.Status != ChecklistStatus.Completed
                            && t.ChecklistRequest.Status != ChecklistStatus.Cancelled
                            && t.Status != ChecklistTaskStatus.Completed
                            && t.Status != ChecklistTaskStatus.NotApplicable
                            && t.AssignedUserId != assigneeId)
                .ToListAsync(ct);
            foreach (var task in openTasks)
            {
                task.AssignedUserId = assigneeId;
                task.AssignedUserName = assignee?.UserName;
                task.ModifiedBy = _currentUser.UserId;
                task.ModifiedAt = DateTime.UtcNow;
            }
        }

        if (changed.Count == 0) return Result.Success();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("SetChecklistApprovers", nameof(ChecklistTemplate), kind.ToString(), string.Join("; ", changed), ct);
        return Result.Success();
    }

    public async Task<ChecklistTemplateEditDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var t = await _db.ChecklistTemplates.AsNoTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (t is null || !CanManage(t.Kind)) return null;

        return new ChecklistTemplateEditDto
        {
            Id = t.Id,
            Kind = t.Kind,
            DepartmentId = t.DepartmentId,
            IsActive = t.IsActive,
            IsRequired = t.IsRequired,
            SortOrder = t.SortOrder,
            Items = t.Items.Where(i => !i.IsDeleted).OrderBy(i => i.SortOrder)
                .Select(i => new ChecklistTemplateItemEditDto
                {
                    Id = i.Id,
                    Description = i.Description,
                    IsRequired = i.IsRequired,
                    SortOrder = i.SortOrder
                }).ToList()
        };
    }

    public async Task<IReadOnlyList<DepartmentOptionDto>> GetDepartmentOptionsAsync(
        ChecklistKind kind, int? includeDepartmentId, CancellationToken ct = default)
    {
        var used = _db.ChecklistTemplates.Where(t => t.Kind == kind && !t.IsDeleted).Select(t => t.DepartmentId);
        return await _db.Departments.AsNoTracking()
            .Where(d => d.Id == includeDepartmentId || (d.IsActive && !used.Contains(d.Id)))
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentOptionDto(d.Id, d.Name))
            .ToListAsync(ct);
    }

    public async Task<Result<int>> CreateAsync(ChecklistTemplateEditDto dto, CancellationToken ct = default)
    {
        var error = await ValidateAsync(dto, ct);
        if (error is not null) return Result<int>.Fail(error);

        var template = new ChecklistTemplate
        {
            Kind = dto.Kind,
            DepartmentId = dto.DepartmentId!.Value,
            IsActive = dto.IsActive,
            IsRequired = dto.IsRequired,
            SortOrder = dto.SortOrder,
            CreatedBy = _currentUser.UserId
        };
        SyncItems(template, dto.Items);
        _db.ChecklistTemplates.Add(template);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("CreateChecklistTemplate", nameof(ChecklistTemplate), template.Id.ToString(), $"{dto.Kind}", ct);
        return Result<int>.Success(template.Id);
    }

    public async Task<Result> UpdateAsync(ChecklistTemplateEditDto dto, CancellationToken ct = default)
    {
        var template = await _db.ChecklistTemplates
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == dto.Id && !t.IsDeleted, ct);
        if (template is null || !CanManage(template.Kind)) return Result.Fail("Template not found.");

        dto.Kind = template.Kind; // the kind of an existing template never changes
        var error = await ValidateAsync(dto, ct);
        if (error is not null) return Result.Fail(error);

        template.DepartmentId = dto.DepartmentId!.Value;
        template.IsActive = dto.IsActive;
        template.IsRequired = dto.IsRequired;
        template.SortOrder = dto.SortOrder;
        template.ModifiedBy = _currentUser.UserId;
        template.ModifiedAt = DateTime.UtcNow;
        SyncItems(template, dto.Items);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("UpdateChecklistTemplate", nameof(ChecklistTemplate), template.Id.ToString(), $"{template.Kind}", ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        var template = await _db.ChecklistTemplates.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct);
        if (template is null || !CanManage(template.Kind)) return Result.Fail("Template not found.");

        _db.ChecklistTemplates.Remove(template); // items cascade; submitted tasks keep their own copies
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("DeleteChecklistTemplate", nameof(ChecklistTemplate), id.ToString(), $"{template.Kind}", ct);
        return Result.Success();
    }

    private async Task<string?> ValidateAsync(ChecklistTemplateEditDto dto, CancellationToken ct)
    {
        if (!Enum.IsDefined(dto.Kind) || !CanManage(dto.Kind)) return "You do not have permission to manage these templates.";
        if (dto.DepartmentId is null) return "Department is required.";
        if (!await _db.Departments.AnyAsync(d => d.Id == dto.DepartmentId, ct)) return "Selected department not found.";
        if (await _db.ChecklistTemplates.AnyAsync(t => t.Kind == dto.Kind && t.DepartmentId == dto.DepartmentId
                                                        && t.Id != dto.Id && !t.IsDeleted, ct))
            return "This department already has a checklist of this kind.";
        if (!dto.Items.Any(i => !string.IsNullOrWhiteSpace(i.Description))) return "Add at least one checklist item.";
        return null;
    }

    /// <summary>Rows with an Id update that item; rows without one are added; missing items are removed.</summary>
    private void SyncItems(ChecklistTemplate template, IEnumerable<ChecklistTemplateItemEditDto> rows)
    {
        var wanted = rows.Where(r => !string.IsNullOrWhiteSpace(r.Description))
            .OrderBy(r => r.SortOrder)
            .Select((r, index) => (Row: r, Order: index + 1))
            .ToList();

        var keptIds = wanted.Where(w => w.Row.Id > 0).Select(w => w.Row.Id).ToHashSet();
        foreach (var removed in template.Items.Where(i => !keptIds.Contains(i.Id)).ToList())
        {
            template.Items.Remove(removed);
            if (removed.Id > 0) _db.ChecklistTemplateItems.Remove(removed);
        }

        foreach (var (row, order) in wanted)
        {
            var item = row.Id > 0 ? template.Items.FirstOrDefault(i => i.Id == row.Id) : null;
            if (item is null)
            {
                item = new ChecklistTemplateItem { CreatedBy = _currentUser.UserId };
                template.Items.Add(item);
            }
            item.Description = row.Description!.Trim();
            item.IsRequired = row.IsRequired;
            item.SortOrder = order;
        }
    }
}

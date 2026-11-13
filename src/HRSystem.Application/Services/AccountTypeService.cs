using System.Text;
using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Domain.Entities;
using HRSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Services;

public class AccountTypeService : IAccountTypeService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IAssignmentService _assignments;

    public AccountTypeService(IAppDbContext db, ICurrentUser currentUser, IAuditService audit, IAssignmentService assignments)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _assignments = assignments;
    }

    private bool CanManage => _currentUser.HasPermission(Permissions.AccountRequestsManage);

    /// <summary>Those who set the departments' handlers and approvers see the page too.</summary>
    private bool CanView => CanManage || _currentUser.HasPermission(Permissions.ChecklistApproversManage);

    public async Task<IReadOnlyList<AccountTemplateListItemDto>> GetListAsync(CancellationToken ct = default)
    {
        if (!CanView) return Array.Empty<AccountTemplateListItemDto>();

        // Active departments, plus inactive ones that still own account types.
        var rows = await _db.Departments.AsNoTracking()
            .Where(d => d.IsActive || _db.AccountTypes.Any(t => t.ResponsibleDeptId == d.Id))
            .OrderBy(d => d.Name)
            .Select(d => new AccountTemplateListItemDto
            {
                DepartmentId = d.Id,
                DepartmentName = d.Name,
                DepartmentIsActive = d.IsActive,
                ActiveCount = _db.AccountTypes.Count(t => t.ResponsibleDeptId == d.Id && t.IsActive),
                InactiveCount = _db.AccountTypes.Count(t => t.ResponsibleDeptId == d.Id && !t.IsActive)
            })
            .ToListAsync(ct);

        var departmentIds = rows.Select(r => r.DepartmentId).ToList();
        var handlers = await _assignments.GetAssigneesAsync(departmentIds, WorkArea.Account, AssignmentRole.Handler, ct);
        var approvers = await _assignments.GetAssigneesAsync(departmentIds, WorkArea.Account, AssignmentRole.Approver, ct);
        foreach (var row in rows)
        {
            row.HandlerNames = AssignmentService.Names(handlers[row.DepartmentId]);
            row.ApproverNames = AssignmentService.Names(approvers[row.DepartmentId]);
        }
        // Departments that own account types first.
        return rows.OrderByDescending(r => r.ActiveCount + r.InactiveCount > 0).ThenBy(r => r.DepartmentName).ToList();
    }

    public async Task<AccountTemplateEditDto?> GetAsync(int departmentId, CancellationToken ct = default)
    {
        if (!CanView) return null;
        var department = await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == departmentId, ct);
        if (department is null) return null;

        return new AccountTemplateEditDto
        {
            DepartmentId = department.Id,
            DepartmentName = department.Name,
            Rows = await _db.AccountTypes.AsNoTracking()
                .Where(t => t.ResponsibleDeptId == departmentId)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
                .Select(t => new AccountTypeRowDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    Code = t.Code,
                    Description = t.Description,
                    Audience = t.Audience,
                    RequiresDetail = t.RequiresDetail,
                    DetailLabel = t.DetailLabel,
                    IsActive = t.IsActive,
                    SortOrder = t.SortOrder
                })
                .ToListAsync(ct)
        };
    }

    public async Task<Result> SaveAsync(AccountTemplateEditDto dto, CancellationToken ct = default)
    {
        if (!CanManage) return Result.Fail("You do not have permission to manage account templates.");
        var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == dto.DepartmentId, ct);
        if (department is null) return Result.Fail("Department not found.");

        var existing = await _db.AccountTypes.Where(t => t.ResponsibleDeptId == dto.DepartmentId).ToListAsync(ct);
        var rows = dto.Rows.Where(r => r.Id > 0 || !string.IsNullOrWhiteSpace(r.Name)).ToList();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Name))
                return Result.Fail("Every account type needs a name. To retire one, untick Active instead of clearing its name.");
            if (row.Name.Trim().Length > 100) return Result.Fail($"\"{row.Name.Trim()[..40]}...\" is longer than 100 characters.");
            if (row.RequiresDetail && string.IsNullOrWhiteSpace(row.DetailLabel))
                return Result.Fail($"\"{row.Name.Trim()}\" asks HR for a detail, so give the detail a label (e.g. Access profile).");
            if (!Enum.IsDefined(row.Audience)) return Result.Fail("Choose which requisition form shows each account type.");
            if (row.Id > 0 && existing.All(t => t.Id != row.Id)) return Result.Fail("An account type was moved or no longer exists. Reload the page.");
        }

        var names = rows.Select(r => r.Name!.Trim()).ToList();
        var duplicate = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) return Result.Fail($"\"{duplicate.Key}\" is listed twice.");

        // Codes are unique across all departments.
        var otherCodes = (await _db.AccountTypes.Where(t => t.ResponsibleDeptId != dto.DepartmentId).Select(t => t.Code).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedCodes = new HashSet<string>(otherCodes, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Where(r => !string.IsNullOrWhiteSpace(r.Code)))
        {
            var code = row.Code!.Trim();
            if (code.Length > 50) return Result.Fail($"Code \"{code[..20]}...\" is longer than 50 characters.");
            if (!usedCodes.Add(code)) return Result.Fail($"Code \"{code}\" is already used by another account type.");
        }

        var changes = new List<string>();
        foreach (var row in rows)
        {
            var type = row.Id > 0 ? existing.First(t => t.Id == row.Id) : null;
            if (type is null)
            {
                type = new AccountType { ResponsibleDeptId = department.Id, CreatedBy = _currentUser.UserId, GroupName = department.Name };
                _db.AccountTypes.Add(type);
                changes.Add($"+{row.Name!.Trim()}");
            }
            else
            {
                type.ModifiedBy = _currentUser.UserId;
                type.ModifiedAt = DateTime.UtcNow;
            }

            type.Name = row.Name!.Trim();
            type.Code = string.IsNullOrWhiteSpace(row.Code) ? UniqueCode(type.Name, usedCodes) : row.Code.Trim();
            type.Description = string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim();
            type.Audience = row.Audience;
            type.RequiresDetail = row.RequiresDetail;
            type.DetailLabel = row.RequiresDetail ? row.DetailLabel!.Trim() : null;
            type.IsActive = row.IsActive;
            type.SortOrder = row.SortOrder;
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("SaveAccountTemplate", nameof(Department), department.Id.ToString(),
            $"{department.Name}: {rows.Count} account type(s) {string.Join(" ", changes)}".Trim(), ct);
        return Result.Success();
    }

    /// <summary>Letters and digits of the name, made unique with a number when needed.</summary>
    private static string UniqueCode(string name, HashSet<string> used)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Where(char.IsLetterOrDigit)) sb.Append(c);
        var stem = sb.Length == 0 ? "Account" : sb.ToString();
        if (stem.Length > 44) stem = stem[..44];

        var code = stem;
        for (var n = 2; !used.Add(code); n++) code = $"{stem}{n}";
        return code;
    }
}

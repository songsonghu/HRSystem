using HRSystem.Application.Common;
using HRSystem.Application.DTOs;

namespace HRSystem.Application.Interfaces;

/// <summary>
/// Templates for Accounts: the account types each department opens and disables. Needs
/// account-requests.manage. Account types are never deleted, because requests and employee
/// accounts refer to them; they are deactivated instead.
/// </summary>
public interface IAccountTypeService
{
    Task<IReadOnlyList<AccountTemplateListItemDto>> GetListAsync(CancellationToken ct = default);
    Task<AccountTemplateEditDto?> GetAsync(int departmentId, CancellationToken ct = default);
    Task<Result> SaveAsync(AccountTemplateEditDto dto, CancellationToken ct = default);
}

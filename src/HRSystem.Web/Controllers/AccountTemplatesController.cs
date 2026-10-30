using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Domain.Enums;
using HRSystem.Web.Services;
using HRSystem.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRSystem.Web.Controllers;

/// <summary>
/// Settings: Templates for Accounts — per department, the account types it opens and disables
/// (account-requests.manage) and who handles and approves them (checklist-approvers.manage).
/// </summary>
[Authorize]
public class AccountTemplatesController : Controller
{
    private const int BlankRows = 3;
    private readonly IAccountTypeService _service;
    private readonly IAssignmentService _assignments;
    private readonly IUserDirectoryService _users;

    public AccountTemplatesController(IAccountTypeService service, IAssignmentService assignments, IUserDirectoryService users)
    {
        _service = service;
        _assignments = assignments;
        _users = users;
    }

    private bool CanEditTypes => User.HasPermission(Permissions.AccountRequestsManage);
    private bool CanEditPeople => User.HasPermission(Permissions.ChecklistApproversManage);

    public async Task<IActionResult> Index()
    {
        if (!CanEditTypes && !CanEditPeople) return Forbid();
        return View(await _service.GetListAsync());
    }

    public async Task<IActionResult> Edit(int id)
    {
        if (!CanEditTypes && !CanEditPeople) return Forbid();
        var dto = await _service.GetAsync(id);
        var assignees = await _assignments.GetDepartmentAsync(id, WorkArea.Account);
        return dto is null || assignees is null ? NotFound() : await EditViewAsync(dto, assignees);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AccountTemplateEditDto dto, List<string>? handlerUserIds, List<string>? approverUserIds)
    {
        if (!CanEditTypes && !CanEditPeople) return Forbid();
        var assignees = await _assignments.GetDepartmentAsync(dto.DepartmentId, WorkArea.Account);
        if (assignees is null) return NotFound();

        if (CanEditTypes)
        {
            var saved = await _service.SaveAsync(dto);
            if (!saved.Succeeded) return await RedisplayAsync(saved.Error!);
        }
        if (CanEditPeople)
        {
            assignees.HandlerUserIds = handlerUserIds ?? new();
            assignees.ApproverUserIds = approverUserIds ?? new();
            var saved = await _assignments.SaveSettingsAsync(WorkArea.Account, new[] { assignees });
            if (!saved.Succeeded)
                return await RedisplayAsync(CanEditTypes ? $"Account types saved, but: {saved.Error}" : saved.Error!);
        }

        TempData["Success"] = $"{dto.DepartmentName} saved.";
        return RedirectToAction(nameof(Index));

        async Task<IActionResult> RedisplayAsync(string error)
        {
            ModelState.AddModelError(string.Empty, error);
            // Keep the people the user picked on the redisplayed form.
            if (CanEditPeople)
            {
                assignees.HandlerUserIds = handlerUserIds ?? new();
                assignees.ApproverUserIds = approverUserIds ?? new();
            }
            if (!CanEditTypes) dto = await _service.GetAsync(dto.DepartmentId) ?? dto;
            return await EditViewAsync(dto, assignees);
        }
    }

    private async Task<IActionResult> EditViewAsync(AccountTemplateEditDto dto, DepartmentAssignmentDto assignees)
    {
        // Offer a few blank rows for new account types; blank rows are ignored when saving.
        var rows = dto.Rows.Where(r => r.Id > 0 || !string.IsNullOrWhiteSpace(r.Name)).ToList();
        if (CanEditTypes)
        {
            var next = rows.Count == 0 ? 1 : rows.Max(r => r.SortOrder) + 1;
            for (var i = 0; i < BlankRows; i++)
                rows.Add(new AccountTypeRowDto { SortOrder = next + i });
        }
        dto.Rows = rows;
        ViewBag.CanEditTypes = CanEditTypes;
        ViewBag.Assignees = await AssigneesEditorViewModel.CreateAsync(assignees, CanEditPeople, _users);
        return View("Edit", dto);
    }
}

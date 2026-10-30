using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using HRSystem.Domain.Enums;
using HRSystem.Web.Services;
using HRSystem.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HRSystem.Web.Controllers;

/// <summary>
/// Settings: Templates for Departure — per department, its departure checklist (departures.manage)
/// and who handles and approves it (checklist-approvers.manage). Onboarding has no department checklists.
/// </summary>
[Authorize]
public class ChecklistTemplatesController : Controller
{
    private const int BlankRows = 5;
    private readonly IChecklistTemplateService _service;
    private readonly IAssignmentService _assignments;
    private readonly IUserDirectoryService _users;

    public ChecklistTemplatesController(IChecklistTemplateService service, IAssignmentService assignments, IUserDirectoryService users)
    {
        _service = service;
        _assignments = assignments;
        _users = users;
    }

    private bool CanEditTemplates => User.HasPermission(Permissions.DeparturesManage);
    private bool CanEditPeople => User.HasPermission(Permissions.ChecklistApproversManage);
    private bool CanView(ChecklistKind kind) => kind == ChecklistKind.Departure && (CanEditTemplates || CanEditPeople);

    public async Task<IActionResult> Index(ChecklistKind kind = ChecklistKind.Departure)
    {
        if (!CanView(kind)) return Forbid();
        ViewBag.Kind = kind;
        ViewBag.CanEditTemplates = CanEditTemplates;
        return View(await _service.GetListAsync(kind));
    }

    public async Task<IActionResult> Create(ChecklistKind kind = ChecklistKind.Departure)
    {
        if (!CanView(kind) || !CanEditTemplates) return Forbid();
        return await EditViewAsync(new ChecklistTemplateEditDto { Kind = kind }, new DepartmentAssignmentDto());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ChecklistTemplateEditDto dto, List<string>? handlerUserIds, List<string>? approverUserIds)
    {
        if (!CanView(dto.Kind) || !CanEditTemplates) return Forbid();
        var picked = Picked(dto.DepartmentId, handlerUserIds, approverUserIds);
        if (!ModelState.IsValid) return await EditViewAsync(dto, picked);

        var result = await _service.CreateAsync(dto);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return await EditViewAsync(dto, picked);
        }

        var people = await SavePeopleAsync(picked);
        if (people is not null)
        {
            TempData["Error"] = $"Checklist template created, but the handlers and approvers were not saved: {people}";
            return RedirectToAction(nameof(Edit), new { id = result.Value });
        }
        TempData["Success"] = "Checklist template created.";
        return RedirectToAction(nameof(Index), new { kind = dto.Kind });
    }

    public async Task<IActionResult> Edit(int id)
    {
        var dto = await _service.GetAsync(id);
        if (dto is null || !CanView(dto.Kind)) return NotFound();
        var assignees = await _assignments.GetDepartmentAsync(dto.DepartmentId!.Value, WorkArea.Departure);
        return assignees is null ? NotFound() : await EditViewAsync(dto, assignees);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ChecklistTemplateEditDto dto, List<string>? handlerUserIds, List<string>? approverUserIds)
    {
        // The template fields are read-only for those who only set people, so read it back for them.
        var current = await _service.GetAsync(dto.Id);
        if (current is null || !CanView(current.Kind)) return NotFound();
        if (!CanEditTemplates) dto = current;
        var picked = Picked(dto.DepartmentId ?? current.DepartmentId, handlerUserIds, approverUserIds);

        if (CanEditTemplates)
        {
            var error = !ModelState.IsValid ? "Please correct the errors below." : (await _service.UpdateAsync(dto)).Error;
            if (error is not null)
            {
                if (ModelState.IsValid) ModelState.AddModelError(string.Empty, error);
                return await EditViewAsync(dto, picked);
            }
        }

        var people = await SavePeopleAsync(picked);
        if (people is not null)
        {
            ModelState.AddModelError(string.Empty, CanEditTemplates ? $"Checklist template saved, but: {people}" : people);
            return await EditViewAsync(dto, picked);
        }

        TempData["Success"] = CanEditTemplates
            ? "Checklist template saved. Requests already submitted keep their original items."
            : "Handlers and approvers saved.";
        return RedirectToAction(nameof(Index), new { kind = dto.Kind });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, ChecklistKind kind)
    {
        if (!CanEditTemplates) return Forbid();
        var result = await _service.DeleteAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Checklist template deleted." : result.Error;
        return RedirectToAction(nameof(Index), new { kind });
    }

    /// <summary>The people posted with the form, on top of the department's current setting.</summary>
    private DepartmentAssignmentDto Picked(int? departmentId, List<string>? handlerUserIds, List<string>? approverUserIds) => new()
    {
        DepartmentId = departmentId ?? 0,
        HandlerUserIds = handlerUserIds ?? new(),
        ApproverUserIds = approverUserIds ?? new()
    };

    /// <summary>Saves the picked handlers and approvers when the user may; returns an error message or null.</summary>
    private async Task<string?> SavePeopleAsync(DepartmentAssignmentDto picked)
    {
        if (!CanEditPeople || picked.DepartmentId == 0) return null;
        var result = await _assignments.SaveSettingsAsync(WorkArea.Departure, new[] { picked });
        return result.Succeeded ? null : result.Error;
    }

    private async Task<IActionResult> EditViewAsync(ChecklistTemplateEditDto dto, DepartmentAssignmentDto picked)
    {
        // Offer a few blank rows for new items; blank rows are ignored when saving.
        var items = dto.Items.Where(i => !string.IsNullOrWhiteSpace(i.Description)).ToList();
        if (CanEditTemplates)
        {
            var next = items.Count == 0 ? 1 : items.Max(i => i.SortOrder) + 1;
            for (int i = 0; i < BlankRows; i++)
                items.Add(new ChecklistTemplateItemEditDto { SortOrder = next + i });
        }
        dto.Items = items;

        // Show the picked people with the department's manager as the fallback.
        if (picked.DepartmentId != 0 && await _assignments.GetDepartmentAsync(picked.DepartmentId, WorkArea.Departure) is { } department)
        {
            picked.DepartmentName = department.DepartmentName;
            picked.ManagerUserName = department.ManagerUserName;
            if (!CanEditPeople)
            {
                picked.HandlerUserIds = department.HandlerUserIds;
                picked.ApproverUserIds = department.ApproverUserIds;
            }
        }

        ViewBag.CanEditTemplates = CanEditTemplates;
        ViewBag.Assignees = await AssigneesEditorViewModel.CreateAsync(picked, CanEditPeople, _users);
        ViewBag.Departments = (await _service.GetDepartmentOptionsAsync(dto.Kind, dto.DepartmentId))
            .Select(d => new SelectListItem(d.Name, d.Id.ToString()))
            .ToList();
        return View("Edit", dto);
    }
}

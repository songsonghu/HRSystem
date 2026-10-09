using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Services;
using HRSystem.Domain.Enums;
using HRSystem.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HRSystem.Web.Controllers;

/// <summary>Department checklist templates; each kind needs its own manage permission.</summary>
[Authorize]
public class ChecklistTemplatesController : Controller
{
    private const int BlankRows = 5;
    private readonly IChecklistTemplateService _service;

    public ChecklistTemplatesController(IChecklistTemplateService service) => _service = service;

    private bool CanManage(ChecklistKind kind)
        => Enum.IsDefined(kind) && User.HasPermission(ChecklistService.ManagePermission(kind));

    public async Task<IActionResult> Index(ChecklistKind kind)
    {
        if (!CanManage(kind)) return Forbid();
        ViewBag.Kind = kind;
        return View(await _service.GetListAsync(kind));
    }

    public async Task<IActionResult> Create(ChecklistKind kind)
    {
        if (!CanManage(kind)) return Forbid();
        return await EditViewAsync(new ChecklistTemplateEditDto { Kind = kind });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ChecklistTemplateEditDto dto)
    {
        if (!CanManage(dto.Kind)) return Forbid();
        if (ModelState.IsValid)
        {
            var result = await _service.CreateAsync(dto);
            if (result.Succeeded)
            {
                TempData["Success"] = "Checklist template created.";
                return RedirectToAction(nameof(Index), new { kind = dto.Kind });
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }
        return await EditViewAsync(dto);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var dto = await _service.GetAsync(id);
        return dto is null ? NotFound() : await EditViewAsync(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ChecklistTemplateEditDto dto)
    {
        if (ModelState.IsValid)
        {
            var result = await _service.UpdateAsync(dto);
            if (result.Succeeded)
            {
                TempData["Success"] = "Checklist template saved. Requests already submitted keep their original items.";
                return RedirectToAction(nameof(Index), new { kind = dto.Kind });
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }
        return await EditViewAsync(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, ChecklistKind kind)
    {
        var result = await _service.DeleteAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Checklist template deleted." : result.Error;
        return RedirectToAction(nameof(Index), new { kind });
    }

    private async Task<IActionResult> EditViewAsync(ChecklistTemplateEditDto dto)
    {
        // Offer a few blank rows for new items; blank rows are ignored when saving.
        var items = dto.Items.Where(i => !string.IsNullOrWhiteSpace(i.Description)).ToList();
        var next = items.Count == 0 ? 1 : items.Max(i => i.SortOrder) + 1;
        for (int i = 0; i < BlankRows; i++)
            items.Add(new ChecklistTemplateItemEditDto { SortOrder = next + i });
        dto.Items = items;

        ViewBag.Departments = (await _service.GetDepartmentOptionsAsync(dto.Kind, dto.DepartmentId))
            .Select(d => new SelectListItem(d.Name, d.Id.ToString()))
            .ToList();
        return View("Edit", dto);
    }
}

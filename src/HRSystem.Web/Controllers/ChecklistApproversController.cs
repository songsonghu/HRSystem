using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Services;
using HRSystem.Domain.Enums;
using HRSystem.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HRSystem.Web.Controllers;

/// <summary>Settings: which login receives each department's onboarding/departure checklist task.</summary>
[Authorize]
public class ChecklistApproversController : Controller
{
    private readonly IChecklistTemplateService _templates;
    private readonly IUserDirectoryService _users;

    public ChecklistApproversController(IChecklistTemplateService templates, IUserDirectoryService users)
    {
        _templates = templates;
        _users = users;
    }

    private bool CanManage(ChecklistKind kind)
        => Enum.IsDefined(kind) && User.HasPermission(ChecklistService.ManagePermission(kind));

    public async Task<IActionResult> Index(ChecklistKind kind = ChecklistKind.Departure)
    {
        if (!CanManage(kind))
        {
            var other = kind == ChecklistKind.Departure ? ChecklistKind.Onboarding : ChecklistKind.Departure;
            return CanManage(other) ? RedirectToAction(nameof(Index), new { kind = other }) : Forbid();
        }
        return await IndexViewAsync(kind, await _templates.GetApproversAsync(kind));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ChecklistKind kind, List<ChecklistApproverDto> rows)
    {
        if (!CanManage(kind)) return Forbid();

        var result = await _templates.SetApproversAsync(kind, rows.ToDictionary(r => r.TemplateId, r => r.ApproverUserId));
        if (result.Succeeded)
        {
            TempData["Success"] = "Approvers saved. Unfinished tasks of open requests were moved to the new approvers.";
            return RedirectToAction(nameof(Index), new { kind });
        }

        ModelState.AddModelError(string.Empty, result.Error!);
        // Keep the user's choices on the redisplayed form.
        var current = await _templates.GetApproversAsync(kind);
        var posted = rows.ToDictionary(r => r.TemplateId, r => r.ApproverUserId);
        foreach (var row in current)
            if (posted.TryGetValue(row.TemplateId, out var chosen)) row.ApproverUserId = chosen;
        return await IndexViewAsync(kind, current);
    }

    private async Task<IActionResult> IndexViewAsync(ChecklistKind kind, IReadOnlyList<ChecklistApproverDto> rows)
    {
        ViewBag.Kind = kind;
        ViewBag.Kinds = new[] { ChecklistKind.Departure, ChecklistKind.Onboarding }.Where(CanManage).ToList();
        var users = (await _users.GetActiveUsersAsync())
            .Select(u => new SelectListItem(u.Display, u.UserId))
            .ToList();
        // A disabled approver stays selectable so saving the page does not silently clear it.
        var missing = rows.Select(r => r.ApproverUserId).Where(id => id is not null && users.All(u => u.Value != id)).Select(id => id!);
        foreach (var user in (await _users.GetByIdsAsync(missing)).Values)
            users.Add(new SelectListItem($"{user.Display} (disabled)", user.UserId));
        ViewBag.Users = users;
        return View("Index", rows.ToList());
    }
}

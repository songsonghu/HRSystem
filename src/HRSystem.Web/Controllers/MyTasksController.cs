using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRSystem.Web.Controllers;

/// <summary>
/// Account Checklist: account request items of the departments the current user handles. The
/// department approvers process items with the same form from Account Approval.
/// </summary>
[Authorize]
public class MyTasksController : Controller
{
    private readonly IAccountRequestService _service;

    public MyTasksController(IAccountRequestService service) => _service = service;

    public async Task<IActionResult> Index() => View(await _service.GetMyPendingItemsAsync());

    /// <summary>"save" keeps the chosen in-progress status; "complete" and "reject" submit the item for approval.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(UpdateItemDto dto, string? submit, bool fromApproval = false)
    {
        dto.Status = submit switch
        {
            "complete" => ItemStatus.Completed,
            "reject" => ItemStatus.Rejected,
            _ => dto.Status
        };
        var result = await _service.UpdateItemAsync(dto);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? dto.Status is ItemStatus.Completed or ItemStatus.Rejected ? "Item submitted to the department approvers." : "Item saved."
            : result.Error;
        return fromApproval
            ? RedirectToAction(nameof(AccountApprovalsController.Index), "AccountApprovals")
            : RedirectToAction(nameof(Index));
    }
}

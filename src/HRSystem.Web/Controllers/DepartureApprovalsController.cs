using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRSystem.Web.Controllers;

/// <summary>
/// Departure Approval: departure tasks of the departments the current user approves, and departures
/// waiting for their final review as head of the leaver's department.
/// </summary>
[Authorize]
public class DepartureApprovalsController : Controller
{
    private readonly IChecklistService _service;

    public DepartureApprovalsController(IChecklistService service) => _service = service;

    public async Task<IActionResult> Index()
    {
        ViewBag.FinalReviews = await _service.GetMyFinalReviewsAsync();
        return View(await _service.GetMyApprovalTasksAsync());
    }

    public async Task<IActionResult> History(ChecklistTaskHistoryQuery query)
    {
        query.Kind = null;
        ViewBag.Query = query;
        ViewBag.FinalReviews = await _service.GetMyFinalReviewHistoryAsync(query);
        return View(await _service.GetMyApprovalHistoryAsync(query));
    }

    /// <summary>Final review of a departure by the head of the leaver's department.</summary>
    public async Task<IActionResult> Review(int id)
    {
        var request = await _service.GetFinalReviewAsync(id);
        return request is null ? NotFound() : View(request);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var result = await _service.FinalizeAsync(id);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Review), new { id });
        }

        TempData["Success"] = "Departure approved. The request is completed and the employee is marked as resigned.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReturnTask(int requestId, int taskId, string? reason)
    {
        var result = await _service.ReturnTaskAsync(taskId, reason ?? string.Empty);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Task returned to the department. The request will come back for your review once it is completed and approved again."
            : result.Error;
        return result.Succeeded ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Review), new { id = requestId });
    }
}

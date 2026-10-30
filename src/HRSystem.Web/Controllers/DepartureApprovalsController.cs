using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRSystem.Web.Controllers;

/// <summary>
/// Departure Approval: departure tasks of the departments the current user approves, departures
/// waiting for them as head of the leaver's department, and (with departures.approve) departures
/// waiting for HR / management approval.
/// </summary>
[Authorize]
public class DepartureApprovalsController : Controller
{
    private readonly IChecklistService _service;

    public DepartureApprovalsController(IChecklistService service) => _service = service;

    public async Task<IActionResult> Index()
    {
        ViewBag.FinalReviews = await _service.GetMyFinalReviewsAsync();
        ViewBag.ManagementApprovals = await _service.GetMyManagementApprovalsAsync();
        return View(await _service.GetMyApprovalTasksAsync());
    }

    public async Task<IActionResult> History(ChecklistTaskHistoryQuery query)
    {
        query.Kind = null;
        ViewBag.Query = query;
        ViewBag.FinalReviews = await _service.GetMyFinalReviewHistoryAsync(query);
        return View(await _service.GetMyApprovalHistoryAsync(query));
    }

    /// <summary>A departure with its tasks and log, for the leaver's department head and HR / management.</summary>
    public async Task<IActionResult> Review(int id)
    {
        var request = await _service.GetFinalReviewAsync(id);
        return request is null ? NotFound() : View(request);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HeadApprove(int id, string? remark)
        => Decided(id, await _service.ApproveByHeadAsync(id, remark ?? string.Empty),
            "Departure approved and sent to HR / management.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ManagementApprove(int id, string? remark)
        => Decided(id, await _service.ApproveByManagementAsync(id, remark ?? string.Empty),
            "Departure approved. The request is completed and the employee is marked as resigned.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReturnToHead(int id, string? reason)
        => Decided(id, await _service.ReturnToHeadAsync(id, reason ?? string.Empty),
            "Departure returned to the department head.");

    private IActionResult Decided(int id, Result result, string success)
    {
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? success : result.Error;
        return result.Succeeded ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Review), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(int id, List<int>? taskIds, string? reason)
    {
        var result = await _service.ReturnTasksAsync(id, taskIds ?? new List<int>(), reason ?? string.Empty);
        return Decided(id, result,
            "Departure returned. It comes back for your review once the chosen departments redo their tasks and their approvers approve them again.");
    }
}

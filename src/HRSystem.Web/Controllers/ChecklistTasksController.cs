using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRSystem.Web.Controllers;

/// <summary>Onboarding and departure checklist tasks assigned to the current user.</summary>
[Authorize]
public class ChecklistTasksController : Controller
{
    private readonly IChecklistService _service;

    public ChecklistTasksController(IChecklistService service) => _service = service;

    public async Task<IActionResult> Index()
    {
        ViewBag.FinalReviews = await _service.GetMyFinalReviewsAsync();
        return View(await _service.GetMyPendingTasksAsync());
    }

    public async Task<IActionResult> History(ChecklistTaskHistoryQuery query)
    {
        ViewBag.Query = query;
        ViewBag.FinalReviews = await _service.GetMyFinalReviewHistoryAsync(query);
        return View(await _service.GetMyTaskHistoryAsync(query));
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
            ? "Task returned to the department. The request will come back for your review once it is completed again."
            : result.Error;
        return result.Succeeded ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Review), new { id = requestId });
    }

    public async Task<IActionResult> Task(int id)
    {
        var task = await _service.GetTaskAsync(id);
        return task is null ? NotFound() : View(task.ToTaskEdit());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Task(ChecklistTaskEditViewModel vm)
    {
        var result = await _service.UpdateTaskAsync(vm.ToUpdateDto());
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            var task = await _service.GetTaskAsync(vm.TaskId);
            if (task is null) return NotFound();
            // Keep what the user entered; only the read-only parts come from the database.
            var redisplay = task.ToTaskEdit();
            redisplay.TaskRemark = vm.TaskRemark;
            foreach (var item in redisplay.Items)
            {
                var posted = vm.Items.FirstOrDefault(i => i.Id == item.Id);
                if (posted is null) continue;
                item.IsCompleted = posted.IsCompleted;
                item.IsNotApplicable = posted.IsNotApplicable;
                item.Remark = posted.Remark;
            }
            return View(redisplay);
        }

        TempData["Success"] = vm.MarkAsCompleted
            ? "Department task completed. It now appears under History."
            : "Progress saved. Click \"Complete Task\" when every required item is done.";
        return vm.MarkAsCompleted ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Task), new { id = vm.TaskId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(int id)
    {
        var result = await _service.ReopenTaskAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Task taken back. Change it and complete it again."
            : result.Error;
        return result.Succeeded ? RedirectToAction(nameof(Task), new { id }) : RedirectToAction(nameof(History));
    }
}

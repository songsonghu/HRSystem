using HRSystem.Application.Common;
using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace HRSystem.Web.Controllers;

/// <summary>
/// Departure Checklist: departure tasks of the departments the current user handles. The task page is
/// shared with the department approvers, who can process, approve or return it.
/// </summary>
[Authorize]
public class ChecklistTasksController : Controller
{
    /// <summary>Room for several attachments of up to 5 MB each in one save.</summary>
    private const long MaxTaskPostBytes = 100 * 1024 * 1024;

    private readonly IChecklistService _service;

    public ChecklistTasksController(IChecklistService service) => _service = service;

    public async Task<IActionResult> Index() => View(await _service.GetMyPendingTasksAsync());

    public async Task<IActionResult> History(ChecklistTaskHistoryQuery query)
    {
        ViewBag.Query = query;
        return View(await _service.GetMyTaskHistoryAsync(query));
    }

    /// <summary>The department approver accepts the completed task.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var result = await _service.ApproveTaskAsync(id);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Task), new { id });
        }
        TempData["Success"] = "Task approved.";
        return RedirectToAction(nameof(DepartureApprovalsController.Index), "DepartureApprovals");
    }

    /// <summary>The department approver sends the completed task back to the handlers.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(int id, string? reason)
    {
        var result = await _service.ReturnTaskToHandlersAsync(id, reason ?? string.Empty);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Task), new { id });
        }
        TempData["Success"] = "Task returned to the handlers.";
        return RedirectToAction(nameof(DepartureApprovalsController.Index), "DepartureApprovals");
    }

    public async Task<IActionResult> Task(int id)
    {
        var task = await _service.GetTaskAsync(id);
        return task is null ? NotFound() : View(task.ToTaskEdit());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxTaskPostBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxTaskPostBytes)]
    public async Task<IActionResult> Task(ChecklistTaskEditViewModel vm)
    {
        var tooLarge = vm.Items.FirstOrDefault(i => i.Attachment?.Length > ChecklistAttachmentRules.MaxBytes);
        var result = tooLarge is not null
            ? Result.Fail($"\"{tooLarge.Attachment!.FileName}\" is larger than 5 MB.")
            : await _service.UpdateTaskAsync(await vm.ToUpdateDtoAsync(HttpContext.RequestAborted));
        if (!result.Succeeded)
        {
            var filesChosen = vm.Items.Any(i => i.Attachment is { Length: > 0 });
            ModelState.AddModelError(string.Empty, result.Error!
                + (filesChosen ? " Nothing was saved, so choose the files again." : string.Empty));
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
                item.RemoveAttachment = posted.RemoveAttachment;
            }
            return View(redisplay);
        }

        if (!vm.MarkAsCompleted)
        {
            TempData["Success"] = "Progress saved. Click \"Complete Task\" when every required item is done.";
            return RedirectToAction(nameof(Task), new { id = vm.TaskId });
        }

        // An approver who completed the task can approve it straight away on the same page.
        var completed = await _service.GetTaskAsync(vm.TaskId);
        if (completed?.CanApprove == true)
        {
            TempData["Success"] = "Department task completed. Approve it, or return it to the handlers.";
            return RedirectToAction(nameof(Task), new { id = vm.TaskId });
        }
        TempData["Success"] = "Department task completed and sent to the department approvers. It now appears under History.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Shows a checklist item's attachment in the browser (images and PDFs only).</summary>
    public async Task<IActionResult> Attachment(int id)
    {
        var file = await _service.GetItemAttachmentAsync(id);
        if (file is null) return NotFound();

        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(file.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.ContentType);
    }
}

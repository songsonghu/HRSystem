using HRSystem.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRSystem.Web.Controllers;

/// <summary>
/// Account Approval: account request items of the departments the current user approves, and
/// employees' own requests waiting for their approval as department manager.
/// </summary>
[Authorize]
public class AccountApprovalsController : Controller
{
    private readonly IAccountRequestService _service;

    public AccountApprovalsController(IAccountRequestService service) => _service = service;

    public async Task<IActionResult> Index()
    {
        ViewBag.PendingRequests = await _service.GetPendingApprovalsAsync();
        return View(await _service.GetMyApprovalItemsAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var result = await _service.ApproveItemAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Item approved." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(int id, string? reason)
    {
        var result = await _service.ReturnItemAsync(id, reason ?? string.Empty);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Item returned to the handlers." : result.Error;
        return RedirectToAction(nameof(Index));
    }
}

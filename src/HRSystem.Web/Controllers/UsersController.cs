using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HRSystem.Web.Controllers;

[Authorize(Policy = Permissions.UsersManage)]
public class UsersController : Controller
{
    private readonly IUserAdminService _service;

    public UsersController(IUserAdminService service) => _service = service;

    public async Task<IActionResult> Index(string? keyword)
    {
        ViewBag.Keyword = keyword;
        return View(await _service.GetListAsync(keyword));
    }

    public async Task<IActionResult> Create()
    {
        await PopulateOptionsAsync(null);
        return View(new UserEditDto());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserEditDto dto)
    {
        if (ModelState.IsValid)
        {
            var result = await _service.CreateAsync(dto);
            if (result.Succeeded)
            {
                TempData["Success"] = $"User {dto.Email} created.";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }

        await PopulateOptionsAsync(null);
        return View(dto);
    }

    public async Task<IActionResult> Edit(string id)
    {
        var dto = await _service.GetAsync(id);
        if (dto is null) return NotFound();

        await PopulateOptionsAsync(id);
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserEditDto dto)
    {
        if (ModelState.IsValid)
        {
            var result = await _service.UpdateAsync(dto);
            if (result.Succeeded)
            {
                TempData["Success"] = $"User {dto.Email} updated.";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }

        await PopulateOptionsAsync(dto.Id);
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordDto dto)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Edit), new { id = dto.Id });
        }

        var result = await _service.ResetPasswordAsync(dto);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Password reset." : result.Error;
        return RedirectToAction(nameof(Edit), new { id = dto.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var result = await _service.DeleteAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "User deleted." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateOptionsAsync(string? userId)
    {
        var options = await _service.GetFormOptionsAsync(userId);
        ViewBag.RoleOptions = options.Roles;
        ViewBag.EmployeeOptions = options.Employees
            .Select(e => new SelectListItem(e.Display, e.Id.ToString()))
            .ToList();
    }
}

using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRSystem.Web.Controllers;

/// <summary>Employee management UI (Module 2). Restricted to Admin/HR.</summary>
[Authorize(Policy = "RequireHR")]
public class EmployeesController : Controller
{
    private readonly IEmployeeService _service;

    public EmployeesController(IEmployeeService service) => _service = service;

    // GET: /Employees?keyword=...
    public async Task<IActionResult> Index(string? keyword)
    {
        var list = await _service.GetListAsync(keyword);
        ViewBag.Keyword = keyword;
        return View(list);
    }

    // GET: /Employees/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var dto = await _service.GetAsync(id);
        return dto is null ? NotFound() : View(dto);
    }

    // GET: /Employees/Create
    public IActionResult Create() => View(new EmployeeEditDto());

    // POST: /Employees/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EmployeeEditDto dto)
    {
        if (!ModelState.IsValid) return View(dto);

        var result = await _service.CreateAsync(dto);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(dto);
        }
        TempData["Success"] = "Employee created.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Employees/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var dto = await _service.GetAsync(id);
        if (dto is null) return NotFound();

        return View(new EmployeeEditDto
        {
            Id = dto.Id,
            EmployeeNo = dto.EmployeeNo,
            Name = dto.Name,
            Email = dto.Email,
            Department = dto.Department,
            Position = dto.Position,
            JoinDate = dto.JoinDate,
            ResignDate = dto.ResignDate,
            Status = dto.Status
        });
    }

    // POST: /Employees/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EmployeeEditDto dto)
    {
        if (!ModelState.IsValid) return View(dto);

        var result = await _service.UpdateAsync(dto);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return View(dto);
        }
        TempData["Success"] = "Employee updated.";
        return RedirectToAction(nameof(Index));
    }

    // POST: /Employees/Delete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _service.DeleteAsync(id);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Employee deleted." : result.Error;
        return RedirectToAction(nameof(Index));
    }
}

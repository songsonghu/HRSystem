using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HRSystem.Web.Controllers;

/// <summary>Employee management UI (Module 2). Restricted to Admin/HR.</summary>
[Authorize(Policy = "RequireHR")]
public class EmployeesController : Controller
{
    private const long MaxAttachmentSize = 20_000_000;

    private static readonly HashSet<string> AllowedAttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".jpg", ".jpeg", ".png"
    };

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

    // GET: /Employees/Attachment/5/3
    public async Task<IActionResult> Attachment(int employeeId, int attachmentId, bool download = false)
    {
        var attachment = await _service.OpenAttachmentAsync(employeeId, attachmentId);
        if (attachment is null) return NotFound();

        return download
            ? File(attachment.Content, attachment.ContentType ?? "application/octet-stream", attachment.FileName)
            : File(attachment.Content, attachment.ContentType ?? "application/octet-stream");
    }

    // GET: /Employees/Create
    public async Task<IActionResult> Create()
    {
        await PopulateDepartmentsAsync();
        return View(new EmployeeEditDto());
    }

    // POST: /Employees/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxAttachmentSize)]
    public async Task<IActionResult> Create(EmployeeEditDto dto, List<IFormFile>? attachments)
    {
        ValidateAttachments(attachments);
        if (!ModelState.IsValid)
        {
            await PopulateDepartmentsAsync();
            return View(dto);
        }

        var result = await _service.CreateAsync(dto);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            await PopulateDepartmentsAsync();
            return View(dto);
        }

        foreach (var attachment in attachments ?? [])
        {
            await using var stream = attachment.OpenReadStream();
            var uploadResult = await _service.AddAttachmentAsync(
                result.Value, stream, attachment.FileName, attachment.ContentType);
            if (!uploadResult.Succeeded)
            {
                TempData["Error"] = $"Employee created, but '{attachment.FileName}' could not be uploaded: {uploadResult.Error}";
                return RedirectToAction(nameof(Details), new { id = result.Value });
            }
        }

        TempData["Success"] = "Employee created.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Employees/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var dto = await _service.GetAsync(id);
        if (dto is null) return NotFound();

        await PopulateDepartmentsAsync();
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
            Status = dto.Status,
            Category = dto.Category,
            Attachments = dto.Attachments
        });
    }

    // POST: /Employees/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxAttachmentSize)]
    public async Task<IActionResult> Edit(EmployeeEditDto dto, List<IFormFile>? replacementAttachments)
    {
        ValidateAttachments(replacementAttachments);
        if (!ModelState.IsValid)
        {
            var employee = await _service.GetAsync(dto.Id);
            dto.Attachments = employee?.Attachments ?? Array.Empty<EmployeeAttachmentDto>();
            await PopulateDepartmentsAsync();
            return View(dto);
        }

        var result = await _service.UpdateAsync(dto);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            var employee = await _service.GetAsync(dto.Id);
            dto.Attachments = employee?.Attachments ?? Array.Empty<EmployeeAttachmentDto>();
            await PopulateDepartmentsAsync();
            return View(dto);
        }

        if (replacementAttachments is { Count: > 0 })
        {
            var uploads = replacementAttachments
                .Select(file => new EmployeeAttachmentUpload(file.OpenReadStream(), file.FileName, file.ContentType))
                .ToList();
            try
            {
                var replacementResult = await _service.ReplaceAttachmentsAsync(dto.Id, uploads);
                if (!replacementResult.Succeeded)
                {
                    TempData["Error"] = replacementResult.Error;
                    return RedirectToAction(nameof(Edit), new { id = dto.Id });
                }
            }
            finally
            {
                foreach (var upload in uploads)
                    await upload.Content.DisposeAsync();
            }
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

    private async Task PopulateDepartmentsAsync()
    {
        var departments = await _service.GetDepartmentNamesAsync();
        ViewBag.Departments = departments
            .Select(name => new SelectListItem(name, name))
            .ToList();
    }

    private void ValidateAttachments(IEnumerable<IFormFile>? attachments)
    {
        foreach (var attachment in attachments ?? [])
        {
            if (attachment.Length == 0)
            {
                ModelState.AddModelError(nameof(attachments), "An uploaded file is empty.");
                continue;
            }

            if (attachment.Length > MaxAttachmentSize)
                ModelState.AddModelError(nameof(attachments), $"'{attachment.FileName}' exceeds the 20 MB limit.");

            if (!AllowedAttachmentExtensions.Contains(Path.GetExtension(attachment.FileName)))
                ModelState.AddModelError(nameof(attachments), $"'{attachment.FileName}' is not an allowed file type.");
        }
    }
}

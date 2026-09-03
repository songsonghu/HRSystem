using HRSystem.Application.Common;
using HRSystem.Application.DTOs;

namespace HRSystem.Application.Interfaces;

public record EmployeeAttachmentUpload(Stream Content, string FileName, string? ContentType);
public record EmployeeAttachmentFile(Stream Content, string FileName, string? ContentType);

/// <summary>Employee management use cases (Module 2).</summary>
public interface IEmployeeService
{
    Task<IReadOnlyList<EmployeeDto>> GetListAsync(string? keyword = null, CancellationToken ct = default);
    Task<EmployeeDto?> GetAsync(int id, CancellationToken ct = default);
    Task<Result<int>> CreateAsync(EmployeeEditDto dto, CancellationToken ct = default);
    Task<Result> UpdateAsync(EmployeeEditDto dto, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
    Task<Result> AddAttachmentAsync(int employeeId, Stream content, string fileName, string? contentType,
        CancellationToken ct = default);
    Task<Result> ReplaceAttachmentsAsync(int employeeId, IReadOnlyList<EmployeeAttachmentUpload> attachments,
        CancellationToken ct = default);
    Task<EmployeeAttachmentFile?> OpenAttachmentAsync(int employeeId, int attachmentId, CancellationToken ct = default);
}

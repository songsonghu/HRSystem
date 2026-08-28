namespace HRSystem.Application.Interfaces;

/// <summary>Writes compliance audit records.</summary>
public interface IAuditService
{
    Task LogAsync(
        string action,
        string? entityName = null,
        string? entityId = null,
        string? detail = null,
        CancellationToken cancellationToken = default);
}

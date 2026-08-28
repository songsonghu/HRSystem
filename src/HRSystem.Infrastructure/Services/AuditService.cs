using HRSystem.Application.Interfaces;
using HRSystem.Domain.Entities;

namespace HRSystem.Infrastructure.Services;

/// <summary>Persists audit records to the database for compliance traceability.</summary>
public class AuditService : IAuditService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public AuditService(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task LogAsync(
        string action, string? entityName = null, string? entityId = null,
        string? detail = null, CancellationToken cancellationToken = default)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _currentUser.UserId,
            UserName = _currentUser.UserName,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Detail = detail,
            Timestamp = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
    }
}

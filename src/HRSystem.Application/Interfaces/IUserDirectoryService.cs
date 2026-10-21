namespace HRSystem.Application.Interfaces;

public interface IUserDirectoryService
{
    Task<UserDirectoryEntry?> GetByIdAsync(string userId, CancellationToken ct = default);

    /// <summary>The given users that exist, keyed by id.</summary>
    Task<IReadOnlyDictionary<string, UserDirectoryEntry>> GetByIdsAsync(IEnumerable<string> userIds, CancellationToken ct = default);

    /// <summary>Logins that are not disabled, ordered by name.</summary>
    Task<IReadOnlyList<UserDirectoryEntry>> GetActiveUsersAsync(CancellationToken ct = default);
}

public class UserDirectoryEntry
{
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? Email { get; set; }

    public string Display => string.IsNullOrWhiteSpace(UserName) || UserName == Email ? Email ?? UserId : $"{UserName} ({Email})";
}

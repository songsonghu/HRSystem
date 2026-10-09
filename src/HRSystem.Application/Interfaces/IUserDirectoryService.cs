namespace HRSystem.Application.Interfaces;

public interface IUserDirectoryService
{
    Task<UserDirectoryEntry?> GetByIdAsync(string userId, CancellationToken ct = default);
}

public class UserDirectoryEntry
{
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? Email { get; set; }
}

using HRSystem.Application.Interfaces;
using HRSystem.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace HRSystem.Infrastructure.Services;

public class UserDirectoryService : IUserDirectoryService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UserDirectoryService(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task<UserDirectoryEntry?> GetByIdAsync(string userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return null;

        return new UserDirectoryEntry
        {
            UserId = user.Id,
            UserName = user.FullName ?? user.UserName,
            Email = user.Email
        };
    }
}

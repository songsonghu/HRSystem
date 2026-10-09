using HRSystem.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Application.Common;

/// <summary>
/// Re-runs an operation from a fresh read when another user changed the same request
/// concurrently (detected by the request's row version). Each attempt must load,
/// validate, apply and save; tracked state is discarded between attempts.
/// </summary>
public static class ConcurrencyRetry
{
    private const int MaxAttempts = 3;

    public static async Task<T> RunAsync<T>(IAppDbContext db, Func<Task<T>> attempt, Func<T> onGiveUp)
    {
        for (int i = 1; ; i++)
        {
            try
            {
                return await attempt();
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                if (i == MaxAttempts) return onGiveUp();
            }
        }
    }

    public static Task<Result> RunAsync(IAppDbContext db, Func<Task<Result>> attempt)
        => RunAsync(db, attempt, () => Result.Fail("The request was changed by someone else at the same time. Please reload and try again."));
}

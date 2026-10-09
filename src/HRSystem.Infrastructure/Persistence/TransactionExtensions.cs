using HRSystem.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace HRSystem.Infrastructure.Persistence;

public static class TransactionExtensions
{
    /// <summary>
    /// Runs <paramref name="work"/> in a transaction that commits only when it returns a
    /// successful <see cref="Result"/>. Wrapped in the execution strategy because the
    /// SQL Server retry strategy rejects user-initiated transactions otherwise.
    /// </summary>
    public static Task<T> InTransactionAsync<T>(this DbContext db, Func<Task<T>> work, CancellationToken ct)
        where T : Result
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await work();
            if (result.Succeeded) await tx.CommitAsync(ct);
            else db.ChangeTracker.Clear();
            return result;
        });
    }
}

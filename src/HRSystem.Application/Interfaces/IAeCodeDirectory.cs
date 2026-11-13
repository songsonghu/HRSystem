using HRSystem.Application.Common;

namespace HRSystem.Application.Interfaces;

/// <summary>Looks up AE accounts in the brokerage system (the internal AeCodeListSearch API).</summary>
public interface IAeCodeDirectory
{
    /// <summary>
    /// Accounts whose AE code matches <paramref name="aeCode"/> and whose name matches <paramref name="aeName"/>;
    /// either may be blank. Fails with a message the user can read when the API cannot be reached.
    /// </summary>
    Task<Result<IReadOnlyList<AeAccountDto>>> SearchAsync(string? aeCode, string? aeName, CancellationToken ct = default);
}

/// <summary>One account found by <see cref="IAeCodeDirectory"/>: the API's UserID, Name and Active.</summary>
public record AeAccountDto(string AeCode, string AeName, bool Active);

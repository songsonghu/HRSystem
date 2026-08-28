namespace HRSystem.Application.Common;

/// <summary>
/// A lightweight operation result used by application services to convey
/// success/failure without throwing for expected business errors.
/// </summary>
public class Result
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }

    public static Result Success() => new() { Succeeded = true };
    public static Result Fail(string error) => new() { Succeeded = false, Error = error };
}

/// <summary>Operation result that also carries a value on success.</summary>
public class Result<T> : Result
{
    public T? Value { get; init; }

    public static Result<T> Success(T value) => new() { Succeeded = true, Value = value };
    public static new Result<T> Fail(string error) => new() { Succeeded = false, Error = error };
}

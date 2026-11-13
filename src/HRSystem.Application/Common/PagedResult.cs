namespace HRSystem.Application.Common;

/// <summary>What the pager shows, whatever the items are.</summary>
public interface IPagedResult
{
    int Page { get; }
    int PageSize { get; }
    int TotalCount { get; }
    int TotalPages { get; }
    int FirstItem { get; }
    int LastItem { get; }
}

/// <summary>One page of a list, with what the pager needs.</summary>
public class PagedResult<T> : IPagedResult
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = Paging.DefaultPageSize;
    public int TotalCount { get; init; }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    /// <summary>1-based position of the first and last item shown, e.g. "11-20 of 45".</summary>
    public int FirstItem => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int LastItem => Math.Min(Page * PageSize, TotalCount);
}

/// <summary>Page sizes offered on the list pages.</summary>
public static class Paging
{
    public const int DefaultPageSize = 10;
    public static readonly IReadOnlyList<int> PageSizes = new[] { 10, 20, 30, 50 };

    /// <summary>One of <see cref="PageSizes"/>; anything else falls back to the default.</summary>
    public static int Size(int? pageSize) => pageSize is int size && PageSizes.Contains(size) ? size : DefaultPageSize;

    /// <summary>A page between 1 and the last page, so a stale link past the end shows the last page.</summary>
    public static int Clamp(int? page, int totalCount, int pageSize)
        => Math.Clamp(page ?? 1, 1, Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize)));
}

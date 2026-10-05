using HRSystem.Domain.Common;

namespace HRSystem.Application.Interfaces;

/// <summary>
/// Generic repository interface for data access operations.
/// Provides a consistent abstraction over EF Core queries.
/// </summary>
public interface IRepository<T> where T : BaseEntity
{
    /// <summary>Get an entity by ID.</summary>
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Get all entities as queryable (for advanced filtering).</summary>
    IQueryable<T> GetAll();

    /// <summary>Add a new entity.</summary>
    Task AddAsync(T entity, CancellationToken ct = default);

    /// <summary>Update an existing entity (tracked by DbContext).</summary>
    void Update(T entity);

    /// <summary>Delete an entity.</summary>
    void Delete(T entity);

    /// <summary>Delete an entity by ID.</summary>
    Task DeleteByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Check if an entity exists.</summary>
    Task<bool> ExistsAsync(int id, CancellationToken ct = default);

    /// <summary>Count total entities.</summary>
    Task<int> CountAsync(CancellationToken ct = default);
}

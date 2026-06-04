using System.Linq.Expressions;

namespace CubeNexus.Application.Interfaces.Repositories;

/// <summary>
/// Generic repository cung cấp các thao tác CRUD cơ bản cho mọi entity.
/// Để test: mock interface này thay vì mock DbContext.
/// </summary>
public interface IRepository<T> where T : class
{
    // ── Read ──────────────────────────────────────────────────
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<List<T>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Tìm entities theo điều kiện. Hỗ trợ LINQ predicate.</summary>
    Task<List<T>> FindAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default);

    Task<T?> FirstOrDefaultAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default);

    Task<bool> AnyAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken ct = default);

    Task<int> CountAsync(
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken ct = default);

    // ── Write ─────────────────────────────────────────────────
    void Add(T entity);
    void AddRange(IEnumerable<T> entities);
    void Update(T entity);
    void Remove(T entity);
    void RemoveRange(IEnumerable<T> entities);
}

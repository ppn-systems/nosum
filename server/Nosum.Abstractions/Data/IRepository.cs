// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Abstractions.Data;

/// <summary>
/// Generic access to one entity type, obtained from an <see cref="IUnitOfWork"/>. Changes are
/// tracked and written only when the unit of work saves. Feature services (farm, inventory,
/// market) build on this instead of touching the database context.
/// </summary>
/// <typeparam name="TEntity">The persisted entity type.</typeparam>
public interface IRepository<TEntity>
    where TEntity : class
{
    /// <summary>Finds an entity by its primary key.</summary>
    /// <param name="key">The primary key value.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The entity, or null when none has that key.</returns>
    ValueTask<TEntity?> FindAsync(object key, CancellationToken cancellationToken);

    /// <summary>Finds the first entity that matches a condition.</summary>
    /// <param name="predicate">The condition, translated to the database.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The entity, or null when none matches.</returns>
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken);

    /// <summary>Checks whether any entity matches a condition.</summary>
    /// <param name="predicate">The condition, translated to the database.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>True when at least one entity matches.</returns>
    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken);

    /// <summary>Marks a new entity to be inserted when the unit of work saves.</summary>
    /// <param name="entity">The entity to insert.</param>
    void Add(TEntity entity);

    /// <summary>Marks an entity to be deleted when the unit of work saves.</summary>
    /// <param name="entity">The entity to delete.</param>
    void Remove(TEntity entity);
}

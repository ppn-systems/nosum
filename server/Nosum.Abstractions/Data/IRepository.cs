// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
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

    /// <summary>Loads every entity that matches a condition; the entities are tracked, so changes to them are saved with the unit of work.</summary>
    /// <param name="predicate">The condition, translated to the database.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching entities, in no particular order; empty when none match.</returns>
    Task<List<TEntity>> ListAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken);

    /// <summary>Checks whether any entity matches a condition.</summary>
    /// <param name="predicate">The condition, translated to the database.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>True when at least one entity matches.</returns>
    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken);

    /// <summary>Counts the entities that match a condition.</summary>
    /// <param name="predicate">The condition, translated to the database.</param>
    /// <param name="cancellationToken">Cancels the count.</param>
    /// <returns>The number of matches.</returns>
    Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken);

    /// <summary>Loads one page of the entities that match a condition, ordered by a key, highest first.</summary>
    /// <typeparam name="TKey">The type of the ordering key.</typeparam>
    /// <param name="predicate">The condition, translated to the database.</param>
    /// <param name="orderByDescending">The ordering key, translated to the database.</param>
    /// <param name="skip">How many matches to skip.</param>
    /// <param name="take">How many matches to load.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page; empty when there are no more matches. The entities are tracked.</returns>
    Task<List<TEntity>> PageAsync<TKey>(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, TKey>> orderByDescending, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Marks a new entity to be inserted when the unit of work saves.</summary>
    /// <param name="entity">The entity to insert.</param>
    void Add(TEntity entity);

    /// <summary>
    /// Marks an entity that this unit of work did not load to be written back when it saves: every column is updated, and the save fails with a concurrency
    /// conflict when no row has that key. For a service that keeps the row's values in memory and so has no tracked entity to change.
    /// </summary>
    /// <param name="entity">The entity with its key and new values; no other entity with the same key may be tracked.</param>
    void Update(TEntity entity);

    /// <summary>Marks an entity to be deleted when the unit of work saves. A new instance that only carries the key is enough; the save fails with a concurrency conflict when no row has that key.</summary>
    /// <param name="entity">The entity to delete.</param>
    void Remove(TEntity entity);
}

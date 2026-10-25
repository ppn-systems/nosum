// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Abstractions.Data;

/// <summary>
/// One short unit of work: load, change, save once. Not thread-safe; create one per operation
/// with <see cref="IUnitOfWorkFactory"/> and dispose it when done.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>Gets the repository for an entity type.</summary>
    /// <typeparam name="TEntity">The persisted entity type.</typeparam>
    /// <returns>A repository bound to this unit of work.</returns>
    IRepository<TEntity> Repository<TEntity>()
        where TEntity : class;

    /// <summary>Writes every tracked change in one transaction.</summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The number of rows written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Tells whether an exception from <see cref="SaveChangesAsync"/> was a unique-key violation,
    /// so a service can turn a lost race into a normal refusal instead of an error.
    /// </summary>
    /// <param name="exception">The exception thrown by a save.</param>
    /// <returns>True when a unique constraint rejected the write.</returns>
    bool IsUniqueViolation(Exception exception);

    /// <summary>
    /// Tells whether an exception from <see cref="SaveChangesAsync"/> means another writer changed or removed a row this unit
    /// had read (optimistic concurrency), so a service can turn a lost race into a normal refusal.
    /// </summary>
    /// <param name="exception">The exception thrown by a save.</param>
    /// <returns>True when a concurrency check rejected the write.</returns>
    bool IsConcurrencyConflict(Exception exception);
}

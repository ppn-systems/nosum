// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Data;

namespace Nosum.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="IRepository{TEntity}"/>.</summary>
/// <typeparam name="TEntity">The persisted entity type.</typeparam>
internal sealed class EfRepository<TEntity> : IRepository<TEntity>
    where TEntity : class
{
    private readonly DbSet<TEntity> _set;

    public EfRepository(DbContext context) => _set = context.Set<TEntity>();

    public ValueTask<TEntity?> FindAsync(object key, CancellationToken cancellationToken)
        => _set.FindAsync([key], cancellationToken);

    public Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken)
        => _set.FirstOrDefaultAsync(predicate, cancellationToken);

    public Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken)
        => _set.AnyAsync(predicate, cancellationToken);

    public void Add(TEntity entity) => _ = _set.Add(entity);

    public void Remove(TEntity entity) => _ = _set.Remove(entity);
}

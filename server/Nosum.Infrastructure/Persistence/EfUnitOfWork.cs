// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nosum.Abstractions.Data;

namespace Nosum.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="IUnitOfWork"/>: one <see cref="NosumDbContext"/> per unit.</summary>
internal sealed class EfUnitOfWork : IUnitOfWork
{
    private const string UniqueViolationSqlState = "23505";

    private readonly NosumDbContext _context;
    private readonly Dictionary<Type, object> _repositories = [];

    public EfUnitOfWork(NosumDbContext context) => _context = context;

    public IRepository<TEntity> Repository<TEntity>()
        where TEntity : class
    {
        if (!_repositories.TryGetValue(typeof(TEntity), out object? repository))
        {
            repository = new EfRepository<TEntity>(_context);
            _repositories[typeof(TEntity)] = repository;
        }

        return (IRepository<TEntity>)repository;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _context.SaveChangesAsync(cancellationToken);

    public bool IsUniqueViolation(Exception exception)
        => exception is DbUpdateException { InnerException: PostgresException { SqlState: UniqueViolationSqlState } };

    public bool IsConcurrencyConflict(Exception exception) => exception is DbUpdateConcurrencyException;

    public ValueTask DisposeAsync() => _context.DisposeAsync();
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Data;

namespace Nosum.Infrastructure.Persistence;

/// <summary>Creates EF Core units of work from shared options.</summary>
public sealed class EfUnitOfWorkFactory : IUnitOfWorkFactory
{
    private readonly DbContextOptions<NosumDbContext> _options;

    /// <summary>Initializes the factory.</summary>
    /// <param name="options">Options from <see cref="NosumDatabase.CreateOptions"/>.</param>
    public EfUnitOfWorkFactory(DbContextOptions<NosumDbContext> options) => _options = options;

    /// <inheritdoc/>
    public IUnitOfWork Create() => new EfUnitOfWork(new NosumDbContext(_options));
}

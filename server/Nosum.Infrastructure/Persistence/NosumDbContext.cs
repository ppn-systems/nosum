// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Microsoft.EntityFrameworkCore;
using Nosum.Domain.Accounts;

namespace Nosum.Infrastructure.Persistence;

/// <summary>The Nosum database. Entity mapping lives in <c>Configurations/</c>, one class per entity.</summary>
public sealed class NosumDbContext : DbContext
{
    /// <summary>Initializes the context.</summary>
    /// <param name="options">Options built by <see cref="NosumDatabase.CreateOptions"/>.</param>
    public NosumDbContext(DbContextOptions<NosumDbContext> options)
        : base(options)
    {
    }

    /// <summary>Gets the player accounts.</summary>
    public DbSet<Player> Players => this.Set<Player>();

    /// <summary>Gets the saved player states.</summary>
    public DbSet<PlayerState> PlayerStates => this.Set<PlayerState>();

    /// <summary>Gets the issued session tokens.</summary>
    public DbSet<PlayerSessionToken> PlayerSessionTokens => this.Set<PlayerSessionToken>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        _ = modelBuilder.ApplyConfigurationsFromAssembly(typeof(NosumDbContext).Assembly);
    }
}

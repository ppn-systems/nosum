// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Microsoft.EntityFrameworkCore.Design;

namespace Nosum.Infrastructure.Persistence;

/// <summary>Used only by the <c>dotnet ef</c> tooling; the host builds its own options.</summary>
internal sealed class NosumDbContextFactory : IDesignTimeDbContextFactory<NosumDbContext>
{
    /// <inheritdoc/>
    public NosumDbContext CreateDbContext(string[] args)
        => new(NosumDatabase.CreateOptions(
            NosumDatabase.ResolveConnectionString(
                Environment.GetEnvironmentVariable(NosumDatabase.ConnectionStringVariable),
                isProduction: false)));
}

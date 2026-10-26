// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Data;

namespace Nosum.Infrastructure.Coops;

/// <summary><see cref="ICoopMembership"/> over the <see cref="CoopDirectory"/>: memory once a player's co-op was read, and a leave or a kick is felt at once because <c>CoopService</c> drops the entries it changed.</summary>
public sealed class CoopMembership : ICoopMembership
{
    private readonly CoopDirectory _directory;

    /// <summary>Initializes the lookup.</summary>
    /// <param name="unitOfWork">Creates units of work for a directory of its own when none is given.</param>
    /// <param name="directory">The shared directory; <see langword="null"/> reads the rows every time.</param>
    public CoopMembership(IUnitOfWorkFactory unitOfWork, CoopDirectory? directory = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _directory = directory ?? new CoopDirectory(unitOfWork, cached: false);
    }

    /// <inheritdoc/>
    public async Task<CoopMembershipInfo?> FindAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        CoopRecord? coop = await _directory.OfPlayerAsync(playerId, cancellationToken).ConfigureAwait(false);
        return coop?.MemberOf(playerId) is { } member ? new CoopMembershipInfo(coop.Id, member.Id, member.Role, coop.LandLevel) : null;
    }
}

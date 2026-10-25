// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Data;
using Nosum.Contracts.Coop;
using Nosum.Domain.Coops;
using Nosum.Infrastructure.Caching;

namespace Nosum.Infrastructure.Coops;

/// <summary>One member of a co-op.</summary>
/// <param name="Id">The membership id.</param>
/// <param name="PlayerId">The player.</param>
/// <param name="Role">Founder or member.</param>
/// <param name="JoinedAt">When the player joined.</param>
public sealed record CoopMemberRecord(Guid Id, Guid PlayerId, CoopRole Role, DateTimeOffset JoinedAt);

/// <summary>A co-op with its members, as the services that only read it need it.</summary>
/// <param name="Id">The co-op id.</param>
/// <param name="Name">The name.</param>
/// <param name="WarehouseLevel">Warehouse upgrade level.</param>
/// <param name="LandLevel">Land upgrade level.</param>
/// <param name="Members">The members, longest-standing first.</param>
public sealed record CoopRecord(Guid Id, string Name, int WarehouseLevel, int LandLevel, IReadOnlyList<CoopMemberRecord> Members)
{
    /// <summary>Finds the membership of a player.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>The member, or <see langword="null"/>.</returns>
    public CoopMemberRecord? MemberOf(Guid playerId) => this.Members.FirstOrDefault(m => m.PlayerId == playerId);
}

/// <summary>
/// The co-op a player belongs to, read through a cache (docs/adr/ADR-035 D5). <c>CoopService</c> is the only writer of <c>coops</c> and <c>coop_members</c> and drops the
/// entries of the co-op and of the players it touched after each change it saved, so a leave or a kick is felt by the next read. The stock, the log and the invitations
/// are shared with several views and are not kept here (D6).
/// </summary>
public sealed class CoopDirectory
{
    private static readonly CoopRecord s_none = new(Guid.Empty, string.Empty, 0, 0, []);

    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ReadThroughCache<Guid, CoopRecord> _byPlayer;

    /// <summary>Initializes the directory.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="cached">Whether lookups are kept; <see langword="false"/> reads the rows every time.</param>
    public CoopDirectory(IUnitOfWorkFactory unitOfWork, bool cached = true)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
        _byPlayer = new ReadThroughCache<Guid, CoopRecord>(cached);
    }

    /// <summary>Gets the co-op a player is in.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The co-op with its members, or <see langword="null"/> for a player in none.</returns>
    public async ValueTask<CoopRecord?> OfPlayerAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        CoopRecord record = await _byPlayer.GetAsync(playerId, ct => this.LoadAsync(playerId, ct), cancellationToken).ConfigureAwait(false);
        return ReferenceEquals(record, s_none) ? null : record;
    }

    /// <summary>Drops what is known about a co-op and about the players that were or are its members.</summary>
    /// <param name="coopId">The co-op that changed.</param>
    /// <param name="playerIds">Players whose membership changed (a joiner has no entry for the co-op yet).</param>
    public void Changed(Guid coopId, params Guid[] playerIds)
    {
        ArgumentNullException.ThrowIfNull(playerIds);
        _byPlayer.InvalidateWhere((_, record) => record.Id == coopId);
        foreach (Guid player in playerIds)
        {
            _byPlayer.Invalidate(player);
        }
    }

    /// <summary>Lets go of a signed-out player's entry.</summary>
    /// <param name="playerId">The player.</param>
    public void Forget(Guid playerId) => _byPlayer.Invalidate(playerId);

    private async Task<CoopRecord> LoadAsync(Guid playerId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            CoopMember? me = await unit.Repository<CoopMember>().FirstOrDefaultAsync(m => m.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
            if (me is null)
            {
                return s_none;
            }

            Guid coopId = me.CoopId;
            Coop? coop = await unit.Repository<Coop>().FindAsync(coopId, cancellationToken).ConfigureAwait(false);
            List<CoopMember> members = await unit.Repository<CoopMember>().ListAsync(m => m.CoopId == coopId, cancellationToken).ConfigureAwait(false);
            return new CoopRecord(
                coopId,
                coop?.Name ?? string.Empty,
                coop?.WarehouseLevel ?? 0,
                coop?.LandLevel ?? 0,
                [.. members.OrderBy(m => m.JoinedAt).Select(m => new CoopMemberRecord(m.Id, m.PlayerId, m.Role, m.JoinedAt))]);
        }
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Chat;
using Nosum.Abstractions.Data;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Coops;
using Nosum.Infrastructure.Friends;
using Nosum.Abstractions.Presence;
using Nosum.Domain.Social;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Visits;

namespace Nosum.Infrastructure.Visits;

/// <summary>
/// <see cref="IInstanceAccessPolicy"/> with visiting (docs/adr/ADR-019): a shared map is open to everybody, a private place to its owner, to the
/// players on the owner's allow-list and, unless the owner turned it off, to the members of the owner's co-op. The decision itself is
/// <see cref="VisitRules.MayEnter"/>; this class only reads the facts. It reads the database on every entry (a visit or a door inside somebody's farm
/// is rare next to walking), so there is nothing cached that could go stale after a revoke.
/// </summary>
public sealed class VisitAccess : IInstanceAccessPolicy
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly INicknameDirectory _players;
    private readonly CoopDirectory _coops;
    private readonly VisitFacts _visits;
    private readonly FriendFacts _friends;
    private readonly IBlockList? _blocks;

    /// <summary>Initializes the policy.</summary>
    /// <param name="unitOfWork">Creates units of work for whatever is not given.</param>
    /// <param name="players">Tells whether the owner exists and is a guest without reading their account row; <see langword="null"/> reads it every time.</param>
    /// <param name="coops">Who is in which co-op; <see langword="null"/> reads the rows every time.</param>
    /// <param name="visits">The owner's allow-list and settings; <see langword="null"/> reads the rows every time.</param>
    /// <param name="friends">The owner's friends; <see langword="null"/> reads the rows every time.</param>
    /// <param name="blocks">The block lists; <see langword="null"/> reads the rows every time.</param>
    public VisitAccess(IUnitOfWorkFactory unitOfWork, INicknameDirectory? players = null, CoopDirectory? coops = null, VisitFacts? visits = null, FriendFacts? friends = null, IBlockList? blocks = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
        _players = players ?? new NicknameDirectory(unitOfWork, cacheFacts: false);
        _coops = coops ?? new CoopDirectory(unitOfWork, cached: false);
        _visits = visits ?? new VisitFacts(unitOfWork, cached: false);
        _friends = friends ?? new FriendFacts(unitOfWork, cached: false);
        _blocks = blocks;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> CanEnterAsync(MapInstance instance, Guid playerId, CancellationToken cancellationToken = default)
    {
        if (!instance.IsPrivate || instance.OwnerId == playerId)
        {
            return true;
        }

        // A mine floor is its owner's alone (docs/adr/ADR-026 D3): visiting is about farms and houses, and nobody else's stones, ore or ladder are ever shown to another player.
        if (GameMaps.TryGet(instance.MapId, out MapSpec? spec) && spec.IsMineFloor)
        {
            return false;
        }

        Guid ownerId = instance.OwnerId;
        PlayerFact? owner = await _players.FindAsync(ownerId, cancellationToken).ConfigureAwait(false);
        if (owner is null)
        {
            return false;
        }

        OwnerVisitFacts facts = await _visits.GetAsync(ownerId, cancellationToken).ConfigureAwait(false);
        bool listed = facts.Listed.Contains(playerId);
        bool blocked = await this.HasBlockedAsync(ownerId, playerId, cancellationToken).ConfigureAwait(false);
        bool isFriend = !listed && !blocked && facts.FriendsMayVisit && (await _friends.GetFriendsAsync(ownerId, cancellationToken).ConfigureAwait(false)).Contains(playerId);
        bool sameCoop = !listed && !isFriend && !blocked && facts.CoopMembersMayVisit && await this.ShareCoopAsync(ownerId, playerId, cancellationToken).ConfigureAwait(false);
        return VisitRules.MayEnter(playerId, ownerId, owner.IsGuest, listed, sameCoop, facts.CoopMembersMayVisit, isFriend, facts.FriendsMayVisit, blocked);
    }

    private async ValueTask<bool> HasBlockedAsync(Guid ownerId, Guid playerId, CancellationToken cancellationToken)
    {
        if (_blocks is not null)
        {
            return await _blocks.HasBlockedAsync(ownerId, playerId, cancellationToken).ConfigureAwait(false);
        }

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            return await unit.Repository<BlockedPlayer>().AnyAsync(b => b.OwnerId == ownerId && b.BlockedId == playerId, cancellationToken).ConfigureAwait(false);
        }
    }

    // Two players share a co-op when the visitor's co-op has the owner as a member too.
    private async Task<bool> ShareCoopAsync(Guid ownerId, Guid visitorId, CancellationToken cancellationToken)
    {
        CoopRecord? coop = await _coops.OfPlayerAsync(visitorId, cancellationToken).ConfigureAwait(false);
        return coop?.MemberOf(ownerId) is not null;
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Presence;
using Nosum.Domain.Accounts;
using Nosum.Domain.Coops;
using Nosum.Domain.Social;
using Nosum.Domain.Visits;
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

    /// <summary>Initializes the policy.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    public VisitAccess(IUnitOfWorkFactory unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> CanEnterAsync(MapInstance instance, Guid playerId, CancellationToken cancellationToken = default)
    {
        if (!instance.IsPrivate || instance.OwnerId == playerId)
        {
            return true;
        }

        Guid ownerId = instance.OwnerId;
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? owner = await unit.Repository<Player>().FindAsync(ownerId, cancellationToken).ConfigureAwait(false);
            if (owner is null)
            {
                return false;
            }

            bool listed = await unit.Repository<VisitGrant>().AnyAsync(g => g.OwnerId == ownerId && g.VisitorId == playerId, cancellationToken).ConfigureAwait(false);
            VisitSettings? settings = await unit.Repository<VisitSettings>().FindAsync(ownerId, cancellationToken).ConfigureAwait(false);
            bool coopMembersMayVisit = settings?.CoopMembersMayVisit ?? VisitRules.CoopMembersMayVisitByDefault;
            bool friendsMayVisit = settings?.FriendsMayVisit ?? VisitRules.FriendsMayVisitByDefault;
            bool blocked = await unit.Repository<BlockedPlayer>().AnyAsync(b => b.OwnerId == ownerId && b.BlockedId == playerId, cancellationToken).ConfigureAwait(false);
            bool isFriend = !listed && !blocked && friendsMayVisit && await unit.Repository<Friendship>().AnyAsync(f => f.PlayerId == ownerId && f.FriendId == playerId, cancellationToken).ConfigureAwait(false);
            bool sameCoop = !listed && !isFriend && !blocked && coopMembersMayVisit && await this.ShareCoopAsync(unit, ownerId, playerId, cancellationToken).ConfigureAwait(false);
            return VisitRules.MayEnter(playerId, ownerId, owner.IsGuest, listed, sameCoop, coopMembersMayVisit, isFriend, friendsMayVisit, blocked);
        }
    }

    // Two players share a co-op when the visitor's membership names a co-op the owner is in too.
    private async Task<bool> ShareCoopAsync(IUnitOfWork unit, Guid ownerId, Guid visitorId, CancellationToken cancellationToken)
    {
        CoopMember? membership = await unit.Repository<CoopMember>().FirstOrDefaultAsync(m => m.PlayerId == visitorId, cancellationToken).ConfigureAwait(false);
        if (membership is null)
        {
            return false;
        }

        Guid coopId = membership.CoopId;
        return await unit.Repository<CoopMember>().AnyAsync(m => m.CoopId == coopId && m.PlayerId == ownerId, cancellationToken).ConfigureAwait(false);
    }
}

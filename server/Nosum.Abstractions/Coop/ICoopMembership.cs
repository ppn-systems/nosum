// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Coop;

namespace Nosum.Abstractions.Coop;

/// <summary>A player's place in their co-op: which one, their membership id (what the audit trail records) and their role.</summary>
/// <param name="CoopId">The co-op's id; it is also the owner of the co-op's land (docs/adr/ADR-020).</param>
/// <param name="MemberId">The membership id; changes if the player leaves and rejoins.</param>
/// <param name="Role">Founder or member.</param>
/// <param name="LandLevel">The co-op's land upgrade level, read with the membership: it decides which tiles of the land may be tilled and built on (docs/adr/ADR-038).</param>
public sealed record CoopMembershipInfo(Guid CoopId, Guid MemberId, CoopRole Role, int LandLevel = 0);

/// <summary>
/// Who belongs to which co-op, read straight from the database on every call so that leaving or being removed takes effect at once.
/// Co-op land asks it before a player enters and before every farm action (docs/adr/ADR-020).
/// </summary>
public interface ICoopMembership
{
    /// <summary>Finds the co-op a player belongs to.</summary>
    /// <param name="playerId">The account id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The membership, or <see langword="null"/> when the player is in no co-op.</returns>
    Task<CoopMembershipInfo?> FindAsync(Guid playerId, CancellationToken cancellationToken = default);
}

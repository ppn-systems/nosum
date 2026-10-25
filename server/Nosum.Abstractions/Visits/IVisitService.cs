// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Visit;
using Nosum.Contracts.Visit.S2C;

namespace Nosum.Abstractions.Visits;

/// <summary>One player in an owner's visitors list.</summary>
/// <param name="PlayerId">The player's account id.</param>
/// <param name="Nickname">The stored nickname.</param>
/// <param name="Listed">Whether the owner put them on the allow-list.</param>
/// <param name="Present">Whether they are in the owner's farm or farmhouse now.</param>
public sealed record VisitorView(Guid PlayerId, string Nickname, bool Listed, bool Present);

/// <summary>An owner's visiting settings and visitors.</summary>
/// <param name="CoopMembersMayVisit">Whether members of the owner's co-op may visit.</param>
/// <param name="IsGuest">Whether the owner is a guest, who keeps no allow-list.</param>
/// <param name="Visitors">The allow-list, plus any unlisted player who is in the place.</param>
public sealed record VisitorsView(bool CoopMembersMayVisit, bool IsGuest, IReadOnlyList<VisitorView> Visitors)
{
    /// <summary>Copies the view into a snapshot packet (the packet's list is cleared first).</summary>
    /// <param name="packet">The packet to fill.</param>
    public void FillSnapshot(VisitorsSnapshot packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        packet.CoopMembersMayVisit = this.CoopMembersMayVisit;
        packet.IsGuest = this.IsGuest;
        packet.Visitors.Clear();
        foreach (VisitorView visitor in this.Visitors)
        {
            VisitorRow row = VisitorRow.Create();
            row.PlayerId = visitor.PlayerId;
            row.Nickname = visitor.Nickname;
            row.Listed = visitor.Listed;
            row.Present = visitor.Present;
            packet.Visitors.Add(row);
        }
    }
}

/// <summary>
/// Visiting another player's farm and farmhouse (docs/adr/ADR-019). The owner keeps an allow-list and a "co-op members may visit" setting;
/// a visit request is checked against them every time, and removing a player (or leaving the co-op) sends them out of the place at once.
/// The server decides everything: the identity comes from the connection and the destination from the nickname, never from the client's idea of a place.
/// </summary>
public interface IVisitService
{
    /// <summary>Reads an owner's settings, allow-list and the players now in their place.</summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The view.</returns>
    Task<VisitorsView> GetAsync(Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>Adds a player, by nickname, to the owner's allow-list.</summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="nickname">The nickname as typed.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<VisitStatus> AddAsync(Guid ownerId, string nickname, CancellationToken cancellationToken = default);

    /// <summary>Removes a player from the allow-list and sends them out of the owner's place if they are in it.</summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="visitorId">The player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<VisitStatus> RemoveAsync(Guid ownerId, Guid visitorId, CancellationToken cancellationToken = default);

    /// <summary>Sends a player out of the owner's place without taking them off the allow-list.</summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="visitorId">The player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<VisitStatus> KickAsync(Guid ownerId, Guid visitorId, CancellationToken cancellationToken = default);

    /// <summary>Turns "co-op members may visit" on or off. Turning it off sends out the members who came in through it.</summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="allowed">The new setting.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<VisitStatus> SetCoopMembersMayVisitAsync(Guid ownerId, bool allowed, CancellationToken cancellationToken = default);

    /// <summary>Moves a player to another player's farm, when the owner let them in. The player must stand on a public map.</summary>
    /// <param name="connection">The visitor's connection.</param>
    /// <param name="visitorId">The visitor (from the server's own sign-in record).</param>
    /// <param name="ownerNickname">The nickname of the owner to visit.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status; on <see cref="VisitStatus.Ok"/> the player was moved and has been told with a map-change push.</returns>
    Task<VisitStatus> VisitAsync(IConnection connection, Guid visitorId, string ownerNickname, CancellationToken cancellationToken = default);
}

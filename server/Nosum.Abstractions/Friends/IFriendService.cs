// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Friends;
using Nosum.Contracts.Friends.S2C;

namespace Nosum.Abstractions.Friends;

/// <summary>One friend as listed: a mutual friend, with where they are now.</summary>
/// <param name="PlayerId">The friend's account id.</param>
/// <param name="Nickname">The friend's nickname.</param>
/// <param name="Online">Whether the friend is online.</param>
/// <param name="MapId">The public map the friend is on; empty when offline or in a private place.</param>
public sealed record FriendView(Guid PlayerId, string Nickname, bool Online, string MapId);

/// <summary>One open friend request.</summary>
/// <param name="PlayerId">The other player's account id.</param>
/// <param name="Nickname">The other player's nickname.</param>
public sealed record FriendRequestView(Guid PlayerId, string Nickname);

/// <summary>A player's friends, open requests and visiting setting.</summary>
/// <param name="FriendsMayVisit">Whether friends may visit without being on the allow-list.</param>
/// <param name="IsGuest">Whether the player is a guest.</param>
/// <param name="Friends">The mutual friends.</param>
/// <param name="Incoming">Requests waiting for the player's answer.</param>
/// <param name="Outgoing">Requests the player sent.</param>
public sealed record FriendsView(bool FriendsMayVisit, bool IsGuest, IReadOnlyList<FriendView> Friends, IReadOnlyList<FriendRequestView> Incoming, IReadOnlyList<FriendRequestView> Outgoing)
{
    /// <summary>Copies the view into a pooled snapshot packet.</summary>
    /// <param name="packet">The packet to fill.</param>
    public void FillSnapshot(FriendsSnapshot packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        packet.FriendsMayVisit = this.FriendsMayVisit;
        packet.IsGuest = this.IsGuest;
        packet.Friends.Clear();
        foreach (FriendView friend in this.Friends)
        {
            FriendRow row = FriendRow.Create();
            row.PlayerId = friend.PlayerId;
            row.Nickname = friend.Nickname;
            row.Online = friend.Online;
            row.MapId = friend.MapId;
            packet.Friends.Add(row);
        }

        Fill(packet.Incoming, this.Incoming);
        Fill(packet.Outgoing, this.Outgoing);
    }

    private static void Fill(List<FriendRequestRow> into, IReadOnlyList<FriendRequestView> from)
    {
        into.Clear();
        foreach (FriendRequestView request in from)
        {
            FriendRequestRow row = FriendRequestRow.Create();
            row.PlayerId = request.PlayerId;
            row.Nickname = request.Nickname;
            into.Add(row);
        }
    }
}

/// <summary>
/// Friends (docs/adr/ADR-029 D4): request and accept by nickname, mutual friends see each other's online status and current public map
/// (pushed when it changes), and friends are a visiting allow-list source (docs/adr/ADR-019) unless the owner turns that off.
/// </summary>
public interface IFriendService
{
    /// <summary>Raised when visiting rights ended: the owner turned "friends may visit" off (visitor <see langword="null"/>) or a friendship ended. The visit service sends out whoever may no longer stay.</summary>
    event Func<Guid, Guid?, ValueTask>? FriendVisitsRevoked;

    /// <summary>Reads the player's friends list.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The view.</returns>
    Task<FriendsView> GetAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Asks a player to be friends; when they already asked, the two become friends.</summary>
    /// <param name="playerId">The asking player.</param>
    /// <param name="nickname">The other player's nickname.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<FriendStatus> RequestAsync(Guid playerId, string nickname, CancellationToken cancellationToken = default);

    /// <summary>Answers a friend request.</summary>
    /// <param name="playerId">The answering player.</param>
    /// <param name="fromId">The player who asked.</param>
    /// <param name="accept">Whether to accept.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<FriendStatus> RespondAsync(Guid playerId, Guid fromId, bool accept, CancellationToken cancellationToken = default);

    /// <summary>Removes a friend, or withdraws an open request in either direction.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="otherId">The other player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<FriendStatus> RemoveAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken = default);

    /// <summary>Turns "friends may visit" on or off.</summary>
    /// <param name="ownerId">The owner.</param>
    /// <param name="allowed">Whether friends may visit.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<FriendStatus> SetFriendsMayVisitAsync(Guid ownerId, bool allowed, CancellationToken cancellationToken = default);

    /// <summary>Tells whether two players are friends.</summary>
    /// <param name="a">One player.</param>
    /// <param name="b">The other player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when they are mutual friends.</returns>
    Task<bool> AreFriendsAsync(Guid a, Guid b, CancellationToken cancellationToken = default);
}

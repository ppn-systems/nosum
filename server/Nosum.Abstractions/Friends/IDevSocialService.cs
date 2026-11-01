// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Abstractions.Friends;

/// <summary>A new registered test player befriended with the asking player, with the session token a second client signs in with.</summary>
/// <param name="Nickname">The test player's nickname.</param>
/// <param name="SessionToken">The test player's session token.</param>
public sealed record DevFriend(string Nickname, string SessionToken);

/// <summary>
/// Development-only tester tool (docs/adr/ADR-097): makes the asking player a registered test account when it is a guest, creates a registered
/// test player, and befriends the two through the normal friend request and its accept. The host registers it only in Development.
/// </summary>
public interface IDevSocialService
{
    /// <summary>Creates the test player and befriends it with <paramref name="playerId"/>.</summary>
    /// <param name="playerId">The asking player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The new friend, or <see langword="null"/> when the player is unknown or the friendship could not be made.</returns>
    Task<DevFriend?> BefriendNewTestPlayerAsync(Guid playerId, CancellationToken cancellationToken = default);
}

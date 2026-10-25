// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking.Packets;

namespace Nosum.Abstractions.Presence;

/// <summary>
/// Pushes a packet to one player's active connection, wherever they are. Any feature (inventory,
/// quests, market) uses it for changes the player did not just ask for. Best effort: a player who is
/// offline simply does not get the push and reads the state when they next ask.
/// </summary>
public interface IPlayerNotifier
{
    /// <summary>Sends a packet to the player's active connection.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="packet">The packet; the caller keeps ownership and may return it to its pool afterwards.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns><see langword="true"/> when the player was connected and the packet was sent.</returns>
    ValueTask<bool> TrySendAsync(Guid playerId, IPacket packet, CancellationToken cancellationToken = default);
}

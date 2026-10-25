// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Gameplay.Maps;

namespace Nosum.Abstractions.Presence;

/// <summary>
/// How a world event reaches players (docs/adr/ADR-022 D4). Every feature that tells players "something changed on the map" chooses one of two doors:
/// <list type="bullet">
/// <item><see cref="PublishAsync"/> (the default): the change is at a place; it goes to the players whose interest set contains the chunk,
/// the ones who can see it. Farm plots, placed objects, resource nodes, ground items, and a player's look or nickname change (at the player's chunk).</item>
/// <item><see cref="PublishInstanceAsync"/> (rare): the change is map-wide, whatever the distance; it goes to every player of the instance.
/// Announcements, chat, a map-wide event.</item>
/// </list>
/// Both are best effort: a dead connection never fails the rest.
/// </summary>
public interface IChunkPublisher
{
    /// <summary>Sends one packet to the players whose interest set contains a chunk.</summary>
    /// <param name="instance">The place.</param>
    /// <param name="chunk">The chunk the change happened in.</param>
    /// <param name="packet">The packet; the caller keeps ownership and may return it to its pool afterwards.</param>
    /// <param name="except">A connection to skip, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when every send finished.</returns>
    ValueTask PublishAsync(MapInstance instance, ChunkCoord chunk, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default);

    /// <summary>Sends one packet to every player of an instance, wherever they stand. Use only for map-wide things.</summary>
    /// <param name="instance">The place.</param>
    /// <param name="packet">The packet; the caller keeps ownership and may return it to its pool afterwards.</param>
    /// <param name="except">A connection to skip, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when every send finished.</returns>
    ValueTask PublishInstanceAsync(MapInstance instance, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default);
}

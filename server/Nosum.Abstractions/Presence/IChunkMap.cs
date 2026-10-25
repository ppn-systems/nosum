// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Maps;

namespace Nosum.Abstractions.Presence;

/// <summary>
/// The server's index from chunk to the players interested in it (docs/adr/ADR-022 D4), kept per instance and changed only when a player's chunk
/// changes. <see cref="IChunkPublisher"/> fans out through it; features that need "who can see this chunk" ask it directly.
/// </summary>
public interface IChunkMap
{
    /// <summary>
    /// Registers a player who just entered an instance: gives them a <see cref="ViewerState"/> with the 3 × 3 block around their chunk and adds them to
    /// the subscribers of those chunks. Does nothing when the player has already left.
    /// </summary>
    /// <param name="player">The player's presence.</param>
    /// <param name="center">The player's chunk.</param>
    /// <param name="bounds">The chunks the map has.</param>
    void Subscribe(PlayerPresence player, ChunkCoord center, ChunkBounds bounds);

    /// <summary>Moves a subscribed player to a new chunk and updates the subscribers of the chunks that joined and left their interest set.</summary>
    /// <param name="player">The player's presence; it must have been subscribed.</param>
    /// <param name="destination">The player's new chunk.</param>
    /// <param name="entered">Receives the chunks that joined (room for <see cref="ChunkInterest.MaxChunks"/>).</param>
    /// <param name="enteredCount">The number of chunks written to <paramref name="entered"/>.</param>
    /// <param name="left">Receives the chunks that left (room for <see cref="ChunkInterest.MaxChunks"/>).</param>
    /// <param name="leftCount">The number of chunks written to <paramref name="left"/>.</param>
    /// <returns><see langword="true"/> when the interest set changed.</returns>
    bool Relocate(PlayerPresence player, ChunkCoord destination, Span<ChunkCoord> entered, out int enteredCount, Span<ChunkCoord> left, out int leftCount);

    /// <summary>Tells whether a chunk is in a subscribed player's interest set.</summary>
    /// <param name="player">The player's presence.</param>
    /// <param name="chunk">The chunk.</param>
    /// <returns><see langword="true"/> when the player is interested in it; <see langword="false"/> too when the player is not subscribed.</returns>
    bool IsInterested(PlayerPresence player, ChunkCoord chunk);

    /// <summary>Appends the players whose interest set contains a chunk to a caller-owned list.</summary>
    /// <param name="instance">The place.</param>
    /// <param name="chunk">The chunk.</param>
    /// <param name="into">List that receives the players.</param>
    void CopySubscribers(MapInstance instance, ChunkCoord chunk, List<PlayerPresence> into);

    /// <summary>
    /// Appends the players a viewer can see: those of the viewer's instance whose chunk (as of the last snapshot tick) is in the viewer's interest set, the viewer included.
    /// Instance-wide membership stays <see cref="IMapPresence.CopyPlayers"/>.
    /// </summary>
    /// <param name="viewer">The viewer's presence.</param>
    /// <param name="into">List that receives the players.</param>
    void CopyVisiblePlayers(PlayerPresence viewer, List<PlayerPresence> into);
}

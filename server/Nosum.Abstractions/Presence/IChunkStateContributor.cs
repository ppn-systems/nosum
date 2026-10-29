// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.World.S2C;
using Nosum.Gameplay.Maps;

namespace Nosum.Abstractions.Presence;

/// <summary>
/// A feature that keeps world state per chunk (farm plots, placed objects, later resource nodes and ground items) and can say what is in one
/// (docs/adr/ADR-022 D4, D5). When a chunk joins a player's interest set the server asks every contributor to append its part to a
/// <see cref="ChunkState"/> and sends the result once; the contributor's own delta packets follow through <see cref="IChunkPublisher"/>.
/// </summary>
public interface IChunkStateContributor
{
    /// <summary>Appends this feature's state of one chunk to a pooled <see cref="ChunkState"/> (its <c>MapId</c>, chunk and clock are already set).</summary>
    /// <param name="instance">The place.</param>
    /// <param name="chunk">The chunk that joined a player's interest set.</param>
    /// <param name="state">The packet to append rows to.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the rows were appended.</returns>
    ValueTask FillAsync(MapInstance instance, ChunkCoord chunk, ChunkState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Brings a player's copy of this feature up to date after a chunk left their interest set (sent right after the chunk's leave notice). A feature whose
    /// entries span chunks sends again the ones that are still visible through another chunk and removes the rest. The default sends nothing.
    /// </summary>
    /// <param name="player">The player; their interest set is already the new one.</param>
    /// <param name="chunk">The chunk that left.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the correction was sent.</returns>
    ValueTask LeftAsync(PlayerPresence player, ChunkCoord chunk, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

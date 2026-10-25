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
}

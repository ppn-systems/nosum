// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;

namespace Nosum.Wasm.Socket.Chunks;

/// <summary>
/// Coordinates initial snapshot loads and pushed chunk state updates for a map-scoped socket channel so that
/// out-of-order <c>ChunkState</c> pushes arriving while a load is in flight (or before the map id is initialized)
/// are accepted and stale snapshot responses are discarded.
/// </summary>
public sealed class ChunkLoadGate
{
    private int _loadGeneration;
    private int _inFlightLoads;

    /// <summary>Begins an asynchronous snapshot load and returns its generation number.</summary>
    /// <returns>The generation number of this load.</returns>
    public int BeginLoad()
    {
        int generation = Interlocked.Increment(ref _loadGeneration);
        _ = Interlocked.Increment(ref _inFlightLoads);
        return generation;
    }

    /// <summary>Marks an asynchronous snapshot load as completed.</summary>
    public void EndLoad() => _ = Interlocked.Decrement(ref _inFlightLoads);

    /// <summary>Returns whether <paramref name="generation"/> is still the latest requested load.</summary>
    /// <param name="generation">The generation number returned by <see cref="BeginLoad"/>.</param>
    /// <returns><see langword="true"/> when no newer load has started.</returns>
    public bool IsCurrent(int generation) => generation == Volatile.Read(ref _loadGeneration);

    /// <summary>
    /// Checks whether a pushed <c>ChunkState</c> packet belongs to the active map or should initialize/replace the active map
    /// while a load is in flight. Call under the channel's lock.
    /// </summary>
    /// <param name="currentMapId">The channel's current map id field.</param>
    /// <param name="packetMapId">The map id carried by the incoming <c>ChunkState</c> packet.</param>
    /// <param name="onMapReset">Callback invoked under the lock to clear stale map items when adopting <paramref name="packetMapId"/>.</param>
    /// <returns><see langword="true"/> when the chunk belongs to the channel's active map and should be applied.</returns>
    public bool TryAcceptChunkMap(ref string currentMapId, string? packetMapId, Action onMapReset)
    {
        ArgumentNullException.ThrowIfNull(currentMapId);
        ArgumentNullException.ThrowIfNull(onMapReset);

        if (string.Equals(packetMapId, currentMapId, StringComparison.Ordinal))
        {
            return true;
        }

        if ((Volatile.Read(ref _inFlightLoads) > 0 || currentMapId.Length == 0) && !string.IsNullOrEmpty(packetMapId))
        {
            onMapReset();
            currentMapId = packetMapId;
            return true;
        }

        return false;
    }
}

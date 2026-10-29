// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;

namespace Nosum.Infrastructure.Placeables;

// The chunk side of the placed objects: which chunks a piece is sent to and published in, and the correction when a chunk leaves a player's set.
public sealed partial class PlaceableService
{
    /// <summary>
    /// A chunk left a player's interest set (docs/adr/ADR-022 D6). The client drops the pieces anchored in it, so a piece that still stands in another chunk the
    /// player sees is sent again, and a piece that now lies in no chunk they see is removed. Called after the chunk's leave notice.
    /// </summary>
    /// <param name="player">The player; their interest set is already the new one.</param>
    /// <param name="chunk">The chunk that left.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the correction was sent.</returns>
    public async ValueTask LeftAsync(PlayerPresence player, ChunkCoord chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (!_maps.TryGet(player.MapId, out MapDefinition? map) || !map.Placeable)
        {
            return;
        }

        InstanceState state = await this.StateOfAsync(player.Instance, map, cancellationToken).ConfigureAwait(false);
        List<PlacedItem> upserted = [];
        List<int> removed = [];
        using (await _locks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
        {
            foreach (PlacedItem item in state.Set.Items)
            {
                ChunkRange range = PlacedChunks.RangeOf(item);
                if (!range.Contains(chunk))
                {
                    continue;
                }

                if (this.AnyInterested(player, range))
                {
                    upserted.Add(item);
                }
                else
                {
                    removed.Add(item.Id);
                }
            }
        }

        if (upserted.Count == 0 && removed.Count == 0)
        {
            return;
        }

        using PacketScope<PlacedChanged> lease = PacketFactory<PlacedChanged>.Acquire();
        FillChanged(lease.Value, player.MapId, upserted, removed);
        await _presence.SendAsync(player.Connection, lease.Value, cancellationToken).ConfigureAwait(false);
    }

    // Whether a player can see any chunk a piece stands in: the piece is shown as soon as one of its chunks is in the interest set.
    private bool AnyInterested(PlayerPresence player, ChunkRange range)
    {
        foreach (ChunkCoord chunk in range.Chunks())
        {
            if (_chunks.IsInterested(player, chunk))
            {
                return true;
            }
        }

        return false;
    }

    // Sends one change to the subscribers of each chunk in the list; the packet is the same for every chunk.
    private async Task PushAsync(MapInstance instance, IEnumerable<ChunkCoord> chunks, IReadOnlyList<PlacedItem> upserted, IReadOnlyList<int> removed, CancellationToken cancellationToken)
    {
        using PacketScope<PlacedChanged> lease = PacketFactory<PlacedChanged>.Acquire();
        FillChanged(lease.Value, instance.MapId, upserted, removed);
        foreach (ChunkCoord chunk in chunks)
        {
            await _publisher.PublishAsync(instance, chunk, lease.Value, null, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void FillChanged(PlacedChanged packet, string mapId, IReadOnlyList<PlacedItem> upserted, IReadOnlyList<int> removed)
    {
        packet.MapId = mapId;
        packet.Upserted.Clear();
        packet.Removed.Clear();
        foreach (PlacedItem item in upserted)
        {
            packet.Upserted.Add(ToRow(item));
        }

        foreach (int id in removed)
        {
            packet.Removed.Add(id);
        }
    }
}

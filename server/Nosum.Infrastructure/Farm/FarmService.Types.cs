// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nosum.Abstractions.Inventory;
using Nosum.Contracts.Farm;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Quests;

namespace Nosum.Infrastructure.Farm;

public sealed partial class FarmService
{
    // The tilled tiles of one instance, in one bucket per chunk (docs/adr/ADR-022 D5): the tiles of a chunk are one lookup away.
    private sealed class MapFarm(MapInstance instance)
    {
        public MapInstance Instance { get; } = instance;

        public Dictionary<ChunkCoord, Dictionary<(int X, int Y), FarmTile>> Chunks { get; } = [];

        public int Count { get; private set; }

        public bool TryGet(int x, int y, out FarmTile tile)
        {
            if (this.Chunks.TryGetValue(ChunkCoord.OfTile(x, y), out Dictionary<(int X, int Y), FarmTile>? bucket))
            {
                return bucket.TryGetValue((x, y), out tile);
            }

            tile = default;
            return false;
        }

        public void Set(int x, int y, FarmTile tile)
        {
            ChunkCoord chunk = ChunkCoord.OfTile(x, y);
            if (!this.Chunks.TryGetValue(chunk, out Dictionary<(int X, int Y), FarmTile>? bucket))
            {
                bucket = [];
                this.Chunks[chunk] = bucket;
            }

            if (bucket.TryAdd((x, y), tile))
            {
                this.Count++;
            }
            else
            {
                bucket[(x, y)] = tile;
            }
        }

        public bool Remove(int x, int y)
        {
            ChunkCoord chunk = ChunkCoord.OfTile(x, y);
            if (!this.Chunks.TryGetValue(chunk, out Dictionary<(int X, int Y), FarmTile>? bucket) || !bucket.Remove((x, y)))
            {
                return false;
            }

            this.Count--;
            if (bucket.Count == 0)
            {
                _ = this.Chunks.Remove(chunk);
            }

            return true;
        }
    }

    private readonly record struct PlannedDrop(int ItemId, int Quantity);

    private readonly record struct Plan(FarmVerdict Verdict, FarmAction Action, FarmTile Next, IReadOnlyList<InventoryOperation> Operations, string Reason, QuestEvent? Report, int AuditItemId = 0, int AuditDelta = 0, PlannedDrop? Drop = null)
    {
        public static Plan Refuse(FarmVerdict verdict) => new(verdict, FarmAction.None, default, [], string.Empty, null);
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Node.S2C;
using Nosum.Domain.Nodes;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;

namespace Nosum.Infrastructure.Nodes;

public sealed partial class ResourceNodeService
{
    /// <inheritdoc/>
    public async Task AdvanceDayAsync(long day, bool seasonChanged, CancellationToken cancellationToken = default)
    {
        foreach (Task<InstanceNodes> load in _instances.Values)
        {
            if (load.IsCompletedSuccessfully)
            {
                // A mine floor is rebuilt from the day when a player enters it; it has nothing to advance.
                InstanceNodes state = await load.ConfigureAwait(false);
                if (!state.Map.IsMineFloor)
                {
                    await this.AdvanceInstanceAsync(state, day, seasonChanged, publish: true, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    // Brings an instance up to a game day: young trees age and may grow, and a public place regrows a few weeds, stones and twigs.
    private async Task AdvanceInstanceAsync(InstanceNodes state, long day, bool seasonChanged, bool publish, CancellationToken cancellationToken)
    {
        List<ResourceNode> changed = [];
        List<int> staleIds = [];
        List<ResourceNode> stale = [];
        using (await _locks.EnterAsync(state.Instance.Key, cancellationToken).ConfigureAwait(false))
        {
            if (day <= state.LastDay)
            {
                return;
            }

            int days = (int)Math.Min(day - state.LastDay, 365);
            List<ResourceNode> young = [];
            foreach (ResourceNode node in state.Field.All)
            {
                if (node is { Kind: NodeKind.Tree, Stage: < TreeStage.Grown })
                {
                    young.Add(node);
                }
            }

            HashSet<(int X, int Y)> occupied = this.OccupiedTiles(state);
            if (_placed is not null)
            {
                occupied.UnionWith(_placed.CoveredTiles(state.Instance));
            }

            List<NodePlacement> placements = NodePlacements.Regrowth(state.Map.NodeSites, state.Field, state.Map.Grid, state.Map.ReservedTiles, occupied, NodeRandom.Mix(state.Seed, day, 2), state.Instance.IsPrivate, seasonChanged);
            foreach (NodePlacement regrown in placements)
            {
                _ = occupied.Add((regrown.X, regrown.Y));
            }

            // Forage (docs/adr/ADR-025 D4): public places only. What is left of another season is taken away, then a few of this season's items appear.
            if (!state.Instance.IsPrivate && state.Map.ForageSites.Count > 0)
            {
                Season season = ForageCatalog.SeasonOfDay(day);
                int standing = 0;
                foreach (ResourceNode node in state.Field.All)
                {
                    if (node.Kind != NodeKind.Forage)
                    {
                        continue;
                    }

                    if (ForageCatalog.IsInSeason(node.Variant, season))
                    {
                        standing++;
                    }
                    else
                    {
                        staleIds.Add(node.Id);
                        stale.Add(node);
                    }
                }

                foreach (NodePlacement foraged in NodePlacements.Forage(state.Map.ForageSites, state.Field, state.Map.Grid, state.Map.ReservedTiles, occupied, NodeRandom.Mix(state.Seed, day, 3), season, standing))
                {
                    if (occupied.Add((foraged.X, foraged.Y)))
                    {
                        placements.Add(foraged);
                    }
                }
            }

            List<(NodePlacement Placement, WorldNode Row)> inserted = [];

            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                IRepository<WorldNode> rows = unit.Repository<WorldNode>();
                if (young.Count > 0)
                {
                    int[] ids = [.. young.ConvertAll(n => n.Id)];
                    foreach (WorldNode row in await rows.ListAsync(r => ids.Contains(r.Id), cancellationToken).ConfigureAwait(false))
                    {
                        ResourceNode aged = NodeRules.Age(ToNode(row), days);
                        Fill(row, aged);
                        _ = state.Field.Replace(aged);
                        changed.Add(aged);
                    }
                }

                if (staleIds.Count > 0)
                {
                    foreach (WorldNode row in await rows.ListAsync(r => staleIds.Contains(r.Id), cancellationToken).ConfigureAwait(false))
                    {
                        rows.Remove(row);
                    }
                }

                DateTimeOffset now = _time.GetUtcNow();
                foreach (NodePlacement placement in placements)
                {
                    WorldNode row = new() { MapId = state.Instance.MapId, OwnerId = state.Instance.OwnerId, TileX = placement.X, TileY = placement.Y, SpawnedAt = now };
                    Fill(row, new ResourceNode(0, placement.X, placement.Y, placement.Kind, placement.Variant, placement.Stage, NodeCatalog.HitsFor(placement.Kind, placement.Stage)));
                    rows.Add(row);
                    inserted.Add((placement, row));
                }

                NodeInstanceState? marker = await unit.Repository<NodeInstanceState>().FirstOrDefaultAsync(s => s.MapId == state.Instance.MapId && s.OwnerId == state.Instance.OwnerId, cancellationToken).ConfigureAwait(false);
                if (marker is not null)
                {
                    marker.LastDay = day;
                }

                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach ((NodePlacement placement, WorldNode row) in inserted)
            {
                ResourceNode node = new(row.Id, placement.X, placement.Y, placement.Kind, placement.Variant, placement.Stage, (byte)row.Hits);
                state.Field.Add(node);
                changed.Add(node);
            }

            foreach (int id in staleIds)
            {
                _ = state.Field.Remove(id);
            }

            state.LastDay = day;
            state.Rebuild();
        }

        if (publish && (changed.Count > 0 || staleIds.Count > 0))
        {
            await this.PublishAsync(state.Instance, changed, stale, cancellationToken).ConfigureAwait(false);
        }
    }

    private HashSet<(int X, int Y)> OccupiedTiles(InstanceNodes state)
    {
        List<PlayerPresence> players = [];
        _presence.CopyPlayers(state.Instance.Key, players);
        HashSet<(int X, int Y)> tiles = [];
        int tileSize = state.Map.Grid.TileSize;
        foreach (PlayerPresence player in players)
        {
            lock (player.Gate)
            {
                _ = tiles.Add(((int)Math.Floor(player.Motion.X / tileSize), (int)Math.Floor(player.Motion.Y / tileSize)));
            }
        }

        return tiles;
    }

    // Loads an instance's nodes once; a failed load is forgotten so the next call tries again.
    private async Task<InstanceNodes> StateOfAsync(MapInstance instance, MapDefinition map, CancellationToken cancellationToken)
    {
        string key = instance.Key;
        Task<InstanceNodes> load = _instances.GetOrAdd(key, _ => this.LoadAsync(instance, map));
        try
        {
            return await load.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (load.IsFaulted)
        {
            _ = _instances.TryRemove(new KeyValuePair<string, Task<InstanceNodes>>(key, load));
            throw;
        }
    }

    private async Task<InstanceNodes> LoadAsync(MapInstance instance, MapDefinition map)
    {
        long today = _days.Today;
        if (map.IsMineFloor)
        {
            return this.BuildMineFloor(instance, map, today);
        }

        InstanceNodes state = new(instance, map, NodeRandom.HashOf(instance.Key));
        long markedDay;
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            NodeInstanceState? marker = await unit.Repository<NodeInstanceState>().FirstOrDefaultAsync(s => s.MapId == instance.MapId && s.OwnerId == instance.OwnerId, CancellationToken.None).ConfigureAwait(false);
            List<WorldNode> rows;
            if (marker is null)
            {
                // The first time the instance exists: fill it from the map, once and never again.
                DateTimeOffset now = _time.GetUtcNow();
                rows = [];
                foreach (NodePlacement placement in NodePlacements.Initial(map.NodeSites, map.Grid, map.ReservedTiles, state.Seed))
                {
                    WorldNode row = new() { MapId = instance.MapId, OwnerId = instance.OwnerId, TileX = placement.X, TileY = placement.Y, SpawnedAt = now };
                    Fill(row, new ResourceNode(0, placement.X, placement.Y, placement.Kind, placement.Variant, placement.Stage, NodeCatalog.HitsFor(placement.Kind, placement.Stage)));
                    rows.Add(row);
                    unit.Repository<WorldNode>().Add(row);
                }

                unit.Repository<NodeInstanceState>().Add(new NodeInstanceState { MapId = instance.MapId, OwnerId = instance.OwnerId, SeededAt = now, LastDay = today });
                _ = await unit.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                markedDay = today;
            }
            else
            {
                rows = await unit.Repository<WorldNode>().ListAsync(n => n.MapId == instance.MapId && n.OwnerId == instance.OwnerId, CancellationToken.None).ConfigureAwait(false);
                markedDay = marker.LastDay;
            }

            foreach (WorldNode row in rows)
            {
                state.Field.Add(ToNode(row));
            }
        }

        state.LastDay = markedDay;
        state.Rebuild();

        // Days that passed while the instance was not loaded (the server was down, or nobody went there): trees grew, a public place regrew once.
        if (today > markedDay)
        {
            await this.AdvanceInstanceAsync(state, today, seasonChanged: false, publish: false, CancellationToken.None).ConfigureAwait(false);
        }

        return state;
    }

    // A mine floor is built from the player, the game day and the floor: the same three always give the same stones, ore and ladder, and nothing is stored (docs/adr/ADR-026 D3).
    private InstanceNodes BuildMineFloor(MapInstance instance, MapDefinition map, long today)
    {
        InstanceNodes state = new(instance, map, MineRules.FloorSeed(instance.OwnerId, today, map.MineFloor)) { LastDay = today };
        (int spawnX, int spawnY) = (WarpRule.TileX(map.SpawnX, map.Grid.TileSize), WarpRule.TileY(map.SpawnY, map.Grid.TileSize));
        MineFloorPlanner.Plan plan = MineFloorPlanner.Build(map.MineFloor, state.Seed, map.Grid, map.ReservedTiles, spawnX, spawnY);
        foreach (NodePlacement placement in plan.Nodes)
        {
            state.Field.Add(new ResourceNode(++state.LastId, placement.X, placement.Y, placement.Kind, placement.Variant, placement.Stage, NodeCatalog.HitsFor(placement.Kind, placement.Stage)));
        }

        state.Rebuild();
        return state;
    }

    // The one place nodes leave for the players: each node change goes to the subscribers of the chunk the node stands in (docs/adr/ADR-022 D4).
    private async ValueTask PublishAsync(MapInstance instance, IReadOnlyList<ResourceNode> upserted, IReadOnlyList<ResourceNode> removed, CancellationToken cancellationToken)
    {
        Dictionary<ChunkCoord, (List<ResourceNode> Upserted, List<int> Removed)> byChunk = [];
        foreach (ResourceNode node in upserted)
        {
            Bucket(byChunk, node).Upserted.Add(node);
        }

        foreach (ResourceNode node in removed)
        {
            Bucket(byChunk, node).Removed.Add(node.Id);
        }

        foreach (KeyValuePair<ChunkCoord, (List<ResourceNode> Upserted, List<int> Removed)> chunk in byChunk)
        {
            using PacketScope<NodeChanged> lease = PacketFactory<NodeChanged>.Acquire();
            NodeChanged packet = lease.Value;
            packet.MapId = instance.MapId;
            packet.Upserted.Clear();
            packet.Removed.Clear();
            foreach (ResourceNode node in chunk.Value.Upserted)
            {
                packet.Upserted.Add(ToRow(node));
            }

            foreach (int id in chunk.Value.Removed)
            {
                packet.Removed.Add(id);
            }

            await _publisher.PublishAsync(instance, chunk.Key, packet, null, cancellationToken).ConfigureAwait(false);
        }
    }

    private static (List<ResourceNode> Upserted, List<int> Removed) Bucket(Dictionary<ChunkCoord, (List<ResourceNode> Upserted, List<int> Removed)> byChunk, ResourceNode node)
    {
        ChunkCoord chunk = ChunkCoord.OfTile(node.X, node.Y);
        if (!byChunk.TryGetValue(chunk, out (List<ResourceNode> Upserted, List<int> Removed) bucket))
        {
            bucket = ([], []);
            byChunk[chunk] = bucket;
        }

        return bucket;
    }

    private sealed class InstanceNodes
    {
        public InstanceNodes(MapInstance instance, MapDefinition map, ulong seed)
        {
            this.Instance = instance;
            this.Map = map;
            this.Seed = seed;
            this.Grid = map.Grid;
        }

        public MapInstance Instance { get; }

        public MapDefinition Map { get; }

        public ulong Seed { get; }

        public NodeField Field { get; } = new();

        public long LastDay { get; set; }

        // Ids of the nodes of a mine floor, which has no database to hand them out.
        public int LastId;

        // Replaced as a whole under the instance lock; movement validation reads it without one.
        public WalkGrid Grid;

        public void Rebuild() => Volatile.Write(ref this.Grid, this.Field.BuildGrid(this.Map.Grid));
    }
}

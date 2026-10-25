// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Placeables;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Node.S2C;
using Nosum.Domain.Nodes;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Nodes;

/// <summary>
/// <see cref="IResourceNodeService"/>: the trees, stones, twigs, ore nodes, weeds and grass of every map instance (docs/adr/ADR-021).
/// An instance's nodes are loaded once (seeded from the map's <c>Paths</c> layer the first time the instance exists), kept in memory in
/// 16 × 16-tile chunk buckets (<see cref="NodeField"/>) and written through to PostgreSQL when they are created, grow, change kind or break.
/// One lock per instance serializes every change, so two players hitting the same stone at once break it once. A breaking node's yield leaves through
/// <see cref="INodeYieldSink"/> in the same transaction as the node's change. The nodes that block walking are part of the grid
/// <see cref="GridFor"/> returns, so server validation and client prediction agree.
/// </summary>
public sealed class ResourceNodeService : IResourceNodeService, INodeTiles
{
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly IInventoryService _inventory;
    private readonly INodeYieldSink _yields;
    private readonly IToolState _tools;
    private readonly IGameDayClock _days;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly IPlacedLookup? _placed;
    private readonly ConcurrentDictionary<string, Task<InstanceNodes>> _instances = new(StringComparer.Ordinal);
    private readonly StripedAsyncLock _locks = new(32);

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands where; every action uses the server's own record of the player.</param>
    /// <param name="maps">The hosted maps and their node markers.</param>
    /// <param name="inventory">Planting a tree seed takes the seed from it.</param>
    /// <param name="yields">Where a breaking node's yield goes.</param>
    /// <param name="tools">The player's can and swing cooldown.</param>
    /// <param name="days">The game day count.</param>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">Clock for the audit timestamps.</param>
    /// <param name="placed">What players placed on the ground (docs/adr/ADR-024); a tree is never planted, and debris never regrows, under a placed piece. <see langword="null"/> when nothing can be placed.</param>
    public ResourceNodeService(IMapPresence presence, IMapCatalog maps, IInventoryService inventory, INodeYieldSink yields, IToolState tools, IGameDayClock days, IUnitOfWorkFactory unitOfWork, TimeProvider time, IPlacedLookup? placed = null)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(yields);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);

        _presence = presence;
        _maps = maps;
        _inventory = inventory;
        _yields = yields;
        _tools = tools;
        _days = days;
        _unitOfWork = unitOfWork;
        _time = time;
        _placed = placed;
    }

    /// <inheritdoc/>
    public async ValueTask PrepareAsync(MapInstance instance, CancellationToken cancellationToken = default)
    {
        if (_maps.TryGet(instance.MapId, out MapDefinition? map) && map.HasNodes)
        {
            _ = await this.StateOfAsync(instance, map, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public WalkGrid GridFor(MapInstance instance, MapDefinition map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return map.HasNodes && _instances.TryGetValue(instance.Key, out Task<InstanceNodes>? load) && load.IsCompletedSuccessfully
            ? Volatile.Read(ref load.Result.Grid)
            : map.Grid;
    }

    /// <inheritdoc/>
    public async Task<NodeSnapshotView> GetAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player))
        {
            return new NodeSnapshotView(string.Empty, NodeLimits.WateringCanCapacity, NodeLimits.WateringCanCapacity, []);
        }

        int water = _tools.WaterLeft(player.AccountId);
        if (!_maps.TryGet(player.MapId, out MapDefinition? map) || !map.HasNodes)
        {
            return new NodeSnapshotView(string.Empty, water, NodeLimits.WateringCanCapacity, []);
        }

        InstanceNodes state = await this.StateOfAsync(player.Instance, map, cancellationToken).ConfigureAwait(false);
        using (await _locks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
        {
            return new NodeSnapshotView(map.Id, water, NodeLimits.WateringCanCapacity, [.. state.Field.All]);
        }
    }

    /// <inheritdoc/>
    public async Task<FarmUseOutcome?> TryUseAsync(NodeUseRequest request, CancellationToken cancellationToken = default)
    {
        PlayerPresence player = request.Player;
        MapDefinition map = request.Map;
        _ = ItemCatalog.TryGet(request.Held.ItemId, out ItemDefinition? item);

        // A can aimed at water is refilled, on any map.
        if (item is { Tool: ToolKind.WateringCan } && map.Water is { } water && water.IsWater(request.TileX, request.TileY))
        {
            if (_tools.WaterLeft(player.AccountId) >= NodeLimits.WateringCanCapacity)
            {
                return Refused(FarmStatus.NothingToDo);
            }

            _ = await _tools.RefillAsync(player.AccountId, cancellationToken).ConfigureAwait(false);
            return new FarmUseOutcome(FarmStatus.Ok, FarmAction.Refilled);
        }

        bool nodeTool = item is { Kind: ItemKind.Tool, Tool: ToolKind.Axe or ToolKind.Pickaxe or ToolKind.Scythe };
        if (!map.HasNodes)
        {
            // Pickaxe and scythe also work on crops and soil, which the farm layer knows; elsewhere there is nothing for a node tool to do.
            return (nodeTool || item is { Kind: ItemKind.TreeSeed }) && map.Tillable is null ? Refused(FarmStatus.NothingToDo) : null;
        }

        InstanceNodes state = await this.StateOfAsync(player.Instance, map, cancellationToken).ConfigureAwait(false);
        using (await _locks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
        {
            if (state.Field.TryGetAt(request.TileX, request.TileY, out ResourceNode node))
            {
                return nodeTool && item is not null
                    ? await this.HitAsync(player, state, node, item.Tool, cancellationToken).ConfigureAwait(false)
                    : Refused(item is null ? FarmStatus.NothingToDo : FarmStatus.Blocked);
            }

            if (item is { Kind: ItemKind.TreeSeed })
            {
                return await this.PlantAsync(request, item, state, cancellationToken).ConfigureAwait(false);
            }

            return nodeTool && map.Tillable is null ? Refused(FarmStatus.NothingToDo) : null;
        }
    }

    /// <inheritdoc/>
    public async Task AdvanceDayAsync(long day, bool seasonChanged, CancellationToken cancellationToken = default)
    {
        foreach (Task<InstanceNodes> load in _instances.Values)
        {
            if (load.IsCompletedSuccessfully)
            {
                await this.AdvanceInstanceAsync(await load.ConfigureAwait(false), day, seasonChanged, publish: true, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static FarmUseOutcome Refused(FarmStatus status) => new(status, FarmAction.None);

    private static FarmStatus ToStatus(InventoryStatus status) => status switch
    {
        InventoryStatus.Ok => FarmStatus.Ok,
        InventoryStatus.NoSpace => FarmStatus.InventoryFull,
        InventoryStatus.Conflict => FarmStatus.Conflict,
        _ => FarmStatus.ItemMissing,
    };

    private static ResourceNode ToNode(WorldNode row)
        => new(row.Id, row.TileX, row.TileY, (NodeKind)row.Kind, (byte)row.Variant, (byte)row.Stage, (byte)row.Hits, row.AgeDays);

    private static void Fill(WorldNode row, in ResourceNode node)
    {
        row.Kind = (short)node.Kind;
        row.Variant = node.Variant;
        row.Stage = node.Stage;
        row.Hits = node.Hits;
        row.AgeDays = node.AgeDays;
    }

    private static NodeRow ToRow(in ResourceNode node)
    {
        NodeRow row = NodeRow.Create();
        row.Id = node.Id;
        row.X = (ushort)node.X;
        row.Y = (ushort)node.Y;
        row.Kind = (byte)node.Kind;
        row.Variant = node.Variant;
        row.Stage = node.Stage;
        row.Hits = node.Hits;
        return row;
    }

    // One swing at a node, under the instance lock. A hit that does not break the node only changes memory and tells the instance; the
    // hit that breaks it delivers the yield and changes the row in one transaction.
    private async Task<FarmUseOutcome> HitAsync(PlayerPresence player, InstanceNodes state, ResourceNode node, ToolKind tool, CancellationToken cancellationToken)
    {
        NodeHit hit = NodeRules.Hit(node, tool);
        switch (hit.Outcome)
        {
            case NodeHitOutcome.WrongTool:
                return Refused(FarmStatus.WrongTool);

            case NodeHitOutcome.Damaged:
                {
                    ResourceNode damaged = hit.Next!.Value;
                    _ = state.Field.Replace(damaged);
                    await this.PublishAsync(player.Instance, [damaged], [], cancellationToken).ConfigureAwait(false);
                    return new FarmUseOutcome(FarmStatus.Ok, FarmAction.Hit);
                }
        }

        ResourceNode? next = hit.Next;
        List<ItemSlot> items = NodeRules.RollYield(node, NodeRandom.Mix(state.Seed, node.Id, (long)node.Kind));
        NodeYieldStatus delivered = await _yields.DeliverAsync(
            new NodeYieldRequest(player.AccountId, player.Instance, node.X, node.Y, items, "node." + NodeKeyOf(node.Kind)),
            async (unit, ct) =>
            {
                IRepository<WorldNode> rows = unit.Repository<WorldNode>();
                WorldNode? row = await rows.FindAsync(node.Id, ct).ConfigureAwait(false);
                if (row is null)
                {
                    return false;
                }

                if (next is { } kept)
                {
                    Fill(row, kept);
                }
                else
                {
                    rows.Remove(row);
                }

                return true;
            },
            cancellationToken).ConfigureAwait(false);

        if (delivered != NodeYieldStatus.Ok)
        {
            return Refused(delivered switch
            {
                NodeYieldStatus.NoSpace => FarmStatus.InventoryFull,
                NodeYieldStatus.Conflict => FarmStatus.Conflict,
                _ => FarmStatus.ItemMissing,
            });
        }

        if (next is { } remains)
        {
            _ = state.Field.Replace(remains);
            state.Rebuild();
            await this.PublishAsync(player.Instance, [remains], [], cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _ = state.Field.Remove(node.Id);
            state.Rebuild();
            await this.PublishAsync(player.Instance, [], [node.Id], cancellationToken).ConfigureAwait(false);
        }

        return new FarmUseOutcome(FarmStatus.Ok, FarmAction.Broke);
    }

    // Planting a tree seed, under the instance lock: free, walkable, unreserved ground that no player stands on; the seed and the new node commit together.
    private async Task<FarmUseOutcome> PlantAsync(NodeUseRequest request, ItemDefinition item, InstanceNodes state, CancellationToken cancellationToken)
    {
        PlayerPresence player = request.Player;
        MapDefinition map = request.Map;
        WalkGrid grid = Volatile.Read(ref state.Grid);
        if (request.TileWorked
            || grid.IsBlocked(request.TileX, request.TileY)
            || map.ReservedTiles.Contains((request.TileX, request.TileY))
            || _placed?.IsCovered(player.Instance, request.TileX, request.TileY) == true
            || state.Field.Count >= NodeLimits.MaxPerInstance)
        {
            return Refused(FarmStatus.Blocked);
        }

        double feetX;
        double feetY;
        lock (player.Gate)
        {
            feetX = player.Motion.X;
            feetY = player.Motion.Y;
        }

        if ((int)Math.Floor(feetX / grid.TileSize) == request.TileX && (int)Math.Floor(feetY / grid.TileSize) == request.TileY)
        {
            return Refused(FarmStatus.Blocked);
        }

        ResourceNode planted = NodeRules.PlantedTree(0, request.TileX, request.TileY, item.TreeVariant);
        WorldNode row = new() { MapId = player.Instance.MapId, OwnerId = player.Instance.OwnerId, TileX = request.TileX, TileY = request.TileY, SpawnedAt = _time.GetUtcNow() };
        Fill(row, planted);
        InventoryChange change = await _inventory.ApplyAsync(
            player.AccountId,
            [InventoryOperation.RemoveFromSlot(request.HotbarSlot, request.Held.ItemId, 1)],
            "node.plant",
            (unit, _) =>
            {
                unit.Repository<WorldNode>().Add(row);
                return Task.FromResult(true);
            },
            cancellationToken).ConfigureAwait(false);
        if (!change.IsOk)
        {
            return Refused(ToStatus(change.Status));
        }

        planted = planted with { Id = row.Id };
        state.Field.Add(planted);
        state.Rebuild();
        await this.PublishAsync(player.Instance, [planted], [], cancellationToken).ConfigureAwait(false);
        return new FarmUseOutcome(FarmStatus.Ok, FarmAction.PlantedTree);
    }

    // Brings an instance up to a game day: young trees age and may grow, and a public place regrows a few weeds, stones and twigs.
    private async Task AdvanceInstanceAsync(InstanceNodes state, long day, bool seasonChanged, bool publish, CancellationToken cancellationToken)
    {
        List<ResourceNode> changed = [];
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

            state.LastDay = day;
            state.Rebuild();
        }

        if (publish && changed.Count > 0)
        {
            await this.PublishAsync(state.Instance, changed, [], cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlySet<(int X, int Y)>> NodeTilesAsync(MapInstance instance, int tileX, int tileY, int width, int height, CancellationToken cancellationToken = default)
    {
        HashSet<(int X, int Y)> found = [];
        if (!_maps.TryGet(instance.MapId, out MapDefinition? map) || !map.HasNodes)
        {
            return found;
        }

        InstanceNodes state = await this.StateOfAsync(instance, map, cancellationToken).ConfigureAwait(false);
        using (await _locks.EnterAsync(instance.Key, cancellationToken).ConfigureAwait(false))
        {
            for (int y = tileY; y < tileY + height; y++)
            {
                for (int x = tileX; x < tileX + width; x++)
                {
                    if (state.Field.HasNodeAt(x, y))
                    {
                        _ = found.Add((x, y));
                    }
                }
            }
        }

        return found;
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
        InstanceNodes state = new(instance, map, NodeRandom.HashOf(instance.Key));
        long today = _days.Today;
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

    private async ValueTask PublishAsync(MapInstance instance, IReadOnlyList<ResourceNode> upserted, IReadOnlyList<int> removed, CancellationToken cancellationToken)
    {
        // The one place nodes leave for the players: an instance-wide multicast today (docs/adr/ADR-022 D4 moves it to the chunk's subscribers).
        using PacketScope<NodeChanged> lease = PacketFactory<NodeChanged>.Acquire();
        NodeChanged packet = lease.Value;
        packet.MapId = instance.MapId;
        packet.Upserted.Clear();
        packet.Removed.Clear();
        foreach (ResourceNode node in upserted)
        {
            packet.Upserted.Add(ToRow(node));
        }

        foreach (int id in removed)
        {
            packet.Removed.Add(id);
        }

        await _presence.MulticastAsync(instance.Key, packet, null, cancellationToken).ConfigureAwait(false);
    }

    private static string NodeKeyOf(NodeKind kind) => NodeCatalog.TryGet(kind, out NodeDefinition? definition) ? definition.Key : "unknown";

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

        // Replaced as a whole under the instance lock; movement validation reads it without one.
        public WalkGrid Grid;

        public void Rebuild() => Volatile.Write(ref this.Grid, this.Field.BuildGrid(this.Map.Grid));
    }
}

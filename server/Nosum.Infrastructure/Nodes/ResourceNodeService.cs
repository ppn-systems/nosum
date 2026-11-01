// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Placeables;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Progress;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Node.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Domain.Nodes;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Skills;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Nodes;

/// <summary>
/// <see cref="IResourceNodeService"/>: the trees, stones, twigs, ore nodes, weeds and grass of every map instance (docs/adr/ADR-021).
/// An instance's nodes are loaded once (seeded from the map's <c>Paths</c> layer the first time the instance exists), kept in memory in
/// 16 × 16-tile chunk buckets (<see cref="NodeField"/>) and written through to PostgreSQL when they are created, grow, change kind or break.
/// One lock per instance serializes every change, so two players hitting the same stone at once break it once. A breaking node's yield leaves through
/// <see cref="INodeYieldSink"/> in the same transaction as the node's change. The nodes that block walking are part of the grid
/// <see cref="GridFor"/> returns, so server validation and client prediction agree. The floors of the mine (docs/adr/ADR-026 D3) are the one exception to persistence: their
/// nodes are built in memory from the player, the game day and the floor when the player enters, never written to PostgreSQL, and dropped by <see cref="Forget"/> when the player leaves.
/// A change is published to the subscribers of the chunk it happened in, and a chunk's nodes are sent once when it joins a player's interest set (docs/adr/ADR-022).
/// </summary>
public sealed partial class ResourceNodeService : IResourceNodeService, IChunkStateContributor, INodeTiles
{
    private readonly IMapPresence _presence;
    private readonly IChunkMap _chunks;
    private readonly IChunkPublisher _publisher;
    private readonly IMapCatalog _maps;
    private readonly IInventoryService _inventory;
    private readonly INodeYieldSink _yields;
    private readonly IToolState _tools;
    private readonly IPlayerProgress _progress;
    private readonly IGameDayClock _days;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly IPlacedLookup? _placed;
    private readonly ConcurrentDictionary<string, Task<InstanceNodes>> _instances = new(StringComparer.Ordinal);
    private readonly StripedAsyncLock _locks = new(32);

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands where; every action uses the server's own record of the player.</param>
    /// <param name="chunks">Who is interested in which chunk; a snapshot reply holds only the nodes in the player's interest set.</param>
    /// <param name="publisher">Delivers a node change to the players who can see its chunk.</param>
    /// <param name="maps">The hosted maps and their node markers.</param>
    /// <param name="inventory">Planting a tree seed takes the seed from it.</param>
    /// <param name="yields">Where a breaking node's yield goes.</param>
    /// <param name="tools">The player's can and swing cooldown.</param>
    /// <param name="days">The game day count.</param>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">Clock for the audit timestamps.</param>
    /// <param name="progress">Every swing at a node spends energy and earns experience through it (docs/adr/ADR-023).</param>
    /// <param name="placed">What players placed on the ground (docs/adr/ADR-024); a tree is never planted, and debris never regrows, under a placed piece. <see langword="null"/> when nothing can be placed.</param>
    public ResourceNodeService(IMapPresence presence, IChunkMap chunks, IChunkPublisher publisher, IMapCatalog maps, IInventoryService inventory, INodeYieldSink yields, IToolState tools, IGameDayClock days, IUnitOfWorkFactory unitOfWork, TimeProvider time, IPlayerProgress progress, IPlacedLookup? placed = null)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(yields);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(progress);

        _presence = presence;
        _chunks = chunks;
        _publisher = publisher;
        _maps = maps;
        _inventory = inventory;
        _yields = yields;
        _tools = tools;
        _days = days;
        _unitOfWork = unitOfWork;
        _time = time;
        _progress = progress;
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
        int capacity = _tools.CapacityOf(player.AccountId);
        if (!_maps.TryGet(player.MapId, out MapDefinition? map) || !map.HasNodes)
        {
            return new NodeSnapshotView(string.Empty, water, capacity, []);
        }

        InstanceNodes state = await this.StateOfAsync(player.Instance, map, cancellationToken).ConfigureAwait(false);
        using (await _locks.EnterAsync(player.Key, cancellationToken).ConfigureAwait(false))
        {
            // Only what the player can see: the nodes of the chunks in their interest set (all of them before the player has one).
            List<ResourceNode> visible = new(state.Field.Count);
            foreach (ResourceNode node in state.Field.All)
            {
                if (player.Viewer is null || _chunks.IsInterested(player, ChunkCoord.OfTile(node.X, node.Y)))
                {
                    visible.Add(Public(node));
                }
            }

            return new NodeSnapshotView(map.Id, water, _tools.CapacityOf(player.AccountId), visible);
        }
    }

    /// <inheritdoc/>
    public async ValueTask FillAsync(MapInstance instance, ChunkCoord chunk, ChunkState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!_maps.TryGet(instance.MapId, out MapDefinition? map) || !map.HasNodes)
        {
            return;
        }

        InstanceNodes nodes = await this.StateOfAsync(instance, map, cancellationToken).ConfigureAwait(false);
        List<ResourceNode> inChunk = [];
        using (await _locks.EnterAsync(instance.Key, cancellationToken).ConfigureAwait(false))
        {
            nodes.Field.CopyChunk(chunk.X, chunk.Y, inChunk);
        }

        foreach (ResourceNode node in inChunk)
        {
            state.Nodes.Add(ToRow(Public(node)));
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
            int capacity = ToolRules.WateringCanCapacity(item.Tier);
            if (_tools.WaterLeft(player.AccountId) >= capacity && _tools.CapacityOf(player.AccountId) >= capacity)
            {
                return Refused(FarmStatus.NothingToDo);
            }

            _ = await _tools.RefillAsync(player.AccountId, capacity, cancellationToken).ConfigureAwait(false);
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
                // A ladder is not broken: using it (facing it, any item in hand) takes the player down; the mine service does the walking (docs/adr/ADR-026 D3).
                if (node.Kind == NodeKind.Ladder)
                {
                    return new FarmUseOutcome(FarmStatus.Ok, FarmAction.Descended);
                }

                // Forage is picked up with an empty hand or whatever is held (docs/adr/ADR-025 D4); no tool, no cooldown.
                if (node.Kind == NodeKind.Forage)
                {
                    return await this.PickForageAsync(player, state, node, cancellationToken).ConfigureAwait(false);
                }

                // An upgraded axe or pickaxe does more damage per swing (docs/adr/ADR-026 D1); the scythe has one tier.
                return nodeTool && item is not null
                    ? await this.HitAsync(player, state, node, item.Tool, item.Tool is ToolKind.Axe or ToolKind.Pickaxe ? ToolRules.DamagePerHit(item.Tier) : 1, item.Tier, cancellationToken).ConfigureAwait(false)
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
    public void Forget(MapInstance instance) => _ = _instances.TryRemove(instance.Key, out _);

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

    // The client never learns which stone hides the ladder (docs/adr/ADR-026 D3): its variant is not sent.
    private static ResourceNode Public(in ResourceNode node) => node is { Kind: NodeKind.Stone, Variant: NodeVariants.LadderStone } ? node with { Variant = 0 } : node;

    private static NodeRow ToRow(in ResourceNode node)
    {
        NodeRow row = NodeRow.Create();
        row.Id = node.Id;
        row.X = (ushort)node.X;
        row.Y = (ushort)node.Y;
        row.Kind = (byte)node.Kind;
        row.Variant = Public(node).Variant;
        row.Stage = node.Stage;
        row.Hits = node.Hits;
        return row;
    }

    // One swing at a node, under the instance lock. A hit that does not break the node only changes memory and tells the instance; the
    // hit that breaks it delivers the yield and changes the row in one transaction.
    private async Task<FarmUseOutcome> HitAsync(PlayerPresence player, InstanceNodes state, ResourceNode node, ToolKind tool, int damage, ToolTier tier, CancellationToken cancellationToken)
    {
        NodeHit hit = NodeRules.Hit(node, tool, damage, tier);
        switch (hit.Outcome)
        {
            case NodeHitOutcome.WrongTool:
                return Refused(FarmStatus.WrongTool);

            case NodeHitOutcome.Damaged:
                {
                    ChargeStatus charged = await _progress.TryChargeAsync(player.AccountId, tool, SkillRules.SkillOf(node.Kind), SkillRules.HitXp, tier, cancellationToken).ConfigureAwait(false);
                    if (charged != ChargeStatus.Ok)
                    {
                        return Refused(charged == ChargeStatus.NoEnergy ? FarmStatus.NoEnergy : FarmStatus.Conflict);
                    }

                    ResourceNode damaged = hit.Next!.Value;
                    _ = state.Field.Replace(damaged);
                    await this.PublishAsync(player.Instance, [damaged], [], cancellationToken).ConfigureAwait(false);
                    return new FarmUseOutcome(FarmStatus.Ok, FarmAction.Hit);
                }
        }

        ResourceNode? next = hit.Next;
        List<ItemSlot> items = NodeRules.RollYield(node, NodeRandom.Mix(state.Seed, node.Id, (long)node.Kind));
        IProgressCharge? charge = null;
        NodeYieldStatus delivered;
        try
        {
            delivered = await _yields.DeliverAsync(
                new NodeYieldRequest(player.AccountId, player.Instance, node.X, node.Y, items, "node." + NodeKeyOf(node.Kind)),
                async (unit, ct) =>
                {
                    // Energy and experience commit with the break; a player without the energy breaks nothing.
                    charge?.Dispose();
                    charge = await _progress.ChargeAsync(unit, player.AccountId, tool, SkillRules.SkillOf(node.Kind), SkillRules.BreakXp(node.Kind), tier, ct).ConfigureAwait(false);
                    if (!charge.Allowed)
                    {
                        return false;
                    }

                    // A mine floor has no rows: its nodes live in memory only.
                    if (state.Map.IsMineFloor)
                    {
                        return true;
                    }

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
            if (delivered == NodeYieldStatus.Ok && charge is not null)
            {
                await charge.PublishAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            charge?.Dispose();
        }

        if (delivered != NodeYieldStatus.Ok)
        {
            if (charge is { Allowed: false })
            {
                return Refused(FarmStatus.NoEnergy);
            }

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
            await this.PublishAsync(player.Instance, [], [node], cancellationToken).ConfigureAwait(false);
        }

        return new FarmUseOutcome(FarmStatus.Ok, FarmAction.Broke, (int)node.Kind);
    }

    // Picking forage up, under the instance lock: the item drops where the node stands like any node yield, and the node is gone.
    private async Task<FarmUseOutcome> PickForageAsync(PlayerPresence player, InstanceNodes state, ResourceNode node, CancellationToken cancellationToken)
    {
        // Breaking a node charges its skill's experience in the same transaction (docs/adr/ADR-023): forage is a foraging node, and a hand costs no energy.
        return await this.HitAsync(player, state, node, ToolKind.None, 1, ToolTier.Basic, cancellationToken).ConfigureAwait(false);
    }

    // Planting a tree seed, under the instance lock: free, walkable, unreserved ground that no player stands on; the seed and the new node commit together.
    private async Task<FarmUseOutcome> PlantAsync(NodeUseRequest request, ItemDefinition item, InstanceNodes state, CancellationToken cancellationToken)
    {
        PlayerPresence player = request.Player;
        MapDefinition map = request.Map;
        WalkGrid grid = Volatile.Read(ref state.Grid);
        if (request.TileWorked
            || map.IsMineFloor
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

    private static string NodeKeyOf(NodeKind kind) => NodeCatalog.TryGet(kind, out NodeDefinition? definition) ? definition.Key : "unknown";
}

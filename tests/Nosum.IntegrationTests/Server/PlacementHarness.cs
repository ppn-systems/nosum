// Copyright (c) 2026 PPN Systems. All rights reserved.

#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Buildings;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Skills;
using Nosum.Contracts.Building.S2C;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Domain.Accounts;
using Nosum.Domain.Coops;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Crafting;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Buildings;
using Nosum.Infrastructure.Coops;
using Nosum.Infrastructure.Crafting;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Maps;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Placeables;
using Nosum.Infrastructure.Visits;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The services of docs/adr/ADR-024 built by hand on a real PostgreSQL database: the inventory, the placeables with a chest and a furnace attached, crafting, and a
/// stub presence that stands players where a test says and records what the server pushed. No sockets: the tests drive the services the handlers call.
/// </summary>
internal sealed class PlacementHarness : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly TmxMapCatalog s_maps = new(GameMaps.All, Path.Combine(AppContext.BaseDirectory, "maps"));

    private readonly PostgresFixture _database;

    public PlacementHarness(PostgresFixture database, INodeTiles? nodes = null, IFarmTiles? farm = null, ISkillLevels? skills = null, Func<PlacementHarness, IFarmTiles>? farmFactory = null, IChunkMap? chunks = null, IChunkPublisher? publisher = null)
    {
        _database = database;
        this.Clock = new FakeTimeProvider(Start);
        this.Units = new EfUnitOfWorkFactory(database.Options);
        this.Inventory = new InventoryService(this.Units, this.Clock);
        this.Presence = new StandingPresence();
        this.Tiles = new PlacedTiles();
        this.Coops = new CoopMembership(this.Units);
        farm = farmFactory?.Invoke(this) ?? farm;
        this.Placeables = new PlaceableService(this.Presence, chunks ?? NoChunks.Instance, publisher ?? new InstanceWidePublisher(this.Presence), s_maps, this.Inventory, this.Units, this.Clock, farm, nodes, this.Coops, this.Tiles);
        this.Chests = new ChestService(this.Presence, s_maps, this.Placeables, this.Inventory, this.Coops, this.Clock);
        this.Placeables.Attach(this.Chests);
        this.Furnaces = new FurnaceService(this.Presence, s_maps, this.Placeables, this.Tiles, this.Placeables, this.Inventory, this.Units, this.Coops, GameCalendar.Default, this.Clock);
        this.Placeables.Attach(this.Furnaces);
        this.Crafting = new CraftingService(this.Presence, this.Inventory, skills ?? new FixedSkillLevels());

        // Farm buildings and animals (docs/adr/ADR-028). Game day one starts at Start, so a test moves the clock a day at a time to get the next day.
        this.Transfers = new TransferService(this.Inventory, this.Clock);
        this.Calendar = new GameCalendar(Start.ToUnixTimeMilliseconds(), GameCalendar.DefaultDayLengthMs);
        this.BuildingDirectory = new BuildingDirectory(this.Units);
        this.Wander = new AnimalWanderService(this.Presence, new InstanceWidePublisher(this.Presence), s_maps, this.BuildingDirectory, this.Units, this.Clock, 20_261_025);
        this.Buildings = new BuildingService(this.Presence, new InstanceWidePublisher(this.Presence), s_maps, this.Inventory, this.Transfers, this.BuildingDirectory, this.Wander, this.Units, this.Coops, this.Calendar, this.Clock);
        this.Placeables.Attach(this.Buildings);
        this.Access = new BuildingAccessPolicy(new VisitAccess(this.Units), this.BuildingDirectory, this.Coops, this.Clock);
    }

    public FakeTimeProvider Clock { get; }

    public EfUnitOfWorkFactory Units { get; }

    public InventoryService Inventory { get; }

    public StandingPresence Presence { get; }

    public PlacedTiles Tiles { get; }

    public CoopMembership Coops { get; }

    public PlaceableService Placeables { get; }

    public ChestService Chests { get; }

    public FurnaceService Furnaces { get; }

    public CraftingService Crafting { get; }

    public TransferService Transfers { get; }

    public GameCalendar Calendar { get; }

    public BuildingDirectory BuildingDirectory { get; }

    public AnimalWanderService Wander { get; }

    public BuildingService Buildings { get; }

    public BuildingAccessPolicy Access { get; }

    public NosumDbContext Db() => new(_database.Options);

    public MapDefinition Map(string mapId)
    {
        _ = s_maps.TryGet(mapId, out MapDefinition? map);
        return map!;
    }

    // A guest by default; an owner who lets others visit must be registered (docs/adr/ADR-019).
    public async Task<Guid> CreatePlayerAsync(bool guest = true)
    {
        IUnitOfWork unit = this.Units.Create();
        await using (unit)
        {
            Player player = TestNicknames.Name(new() { IsGuest = guest, CreatedAt = Start, LastActiveAt = Start });
            unit.Repository<Player>().Add(player);
            _ = await unit.SaveChangesAsync(CancellationToken.None);
            return player.Id;
        }
    }

    // A row of open, unreserved tiles on a map near its spawn: the first is where the objects go, the player stands on the last.
    public (int X, int Y, int Width) OpenRow(string mapId, int width)
    {
        MapDefinition map = this.Map(mapId);
        int startX = (int)(map.SpawnX / 16);
        int startY = (int)(map.SpawnY / 16);
        for (int radius = 0; radius < 40; radius++)
        {
            for (int y = startY - radius; y <= startY + radius; y++)
            {
                for (int x = startX - radius; x <= startX + radius; x++)
                {
                    bool open = true;
                    for (int i = 0; i < width && open; i++)
                    {
                        open = !map.Grid.IsBlocked(x + i, y) && !map.ReservedTiles.Contains((x + i, y));
                    }

                    if (open)
                    {
                        return (x, y, width);
                    }
                }
            }
        }

        throw new InvalidOperationException("No open row on " + mapId);
    }

    // A tillable, open, unreserved tile of the map with an open tile to its right to stand on.
    public (int X, int Y) TillableSpot(string mapId)
    {
        MapDefinition map = this.Map(mapId);
        int startX = (int)(map.SpawnX / 16);
        int startY = (int)(map.SpawnY / 16);
        for (int radius = 0; radius < 60; radius++)
        {
            for (int y = startY - radius; y <= startY + radius; y++)
            {
                for (int x = startX - radius; x <= startX + radius; x++)
                {
                    if (map.Tillable!.IsTillable(x, y) && !map.Grid.IsBlocked(x, y) && !map.Grid.IsBlocked(x + 1, y) && !map.ReservedTiles.Contains((x, y)) && !map.ReservedTiles.Contains((x + 1, y)))
                    {
                        return (x, y);
                    }
                }
            }
        }

        throw new InvalidOperationException("No tillable spot on " + mapId);
    }

    // An open tile next to a tile the map blocks (a wall of the farmhouse, a tree), and the blocked tile.
    public ((int X, int Y) Open, (int X, int Y) Blocked) WallSpot(string mapId)
    {
        MapDefinition map = this.Map(mapId);
        int startX = (int)(map.SpawnX / 16);
        int startY = (int)(map.SpawnY / 16);
        for (int radius = 1; radius < 60; radius++)
        {
            for (int y = startY - radius; y <= startY + radius; y++)
            {
                for (int x = startX - radius; x <= startX + radius; x++)
                {
                    if (map.Grid.IsBlocked(x, y) && x > 0 && y > 0 && x < map.Grid.Width - 1 && y < map.Grid.Height - 1 && !map.Grid.IsBlocked(x + 1, y) && !map.ReservedTiles.Contains((x + 1, y)))
                    {
                        return ((x + 1, y), (x, y));
                    }
                }
            }
        }

        throw new InvalidOperationException("No wall on " + mapId);
    }

    // A reserved tile that is open ground, and an open tile beside it to stand on.
    public ((int X, int Y) Open, (int X, int Y) Reserved) ReservedSpot(string mapId)
    {
        MapDefinition map = this.Map(mapId);
        foreach ((int x, int y) in map.ReservedTiles.OrderBy(t => t.X).ThenBy(t => t.Y))
        {
            if (!map.Grid.IsBlocked(x, y) && !map.Grid.IsBlocked(x + 1, y) && !map.ReservedTiles.Contains((x + 1, y)))
            {
                return ((x + 1, y), (x, y));
            }
        }

        throw new InvalidOperationException("No reserved tile on " + mapId);
    }

    // A free rectangle of ground near the map's spawn (and the tile in front of its bottom row): where a building may stand. Skips the first <paramref name="skip"/> rectangles that do not overlap.
    public (int X, int Y) OpenArea(string mapId, int width, int height, int skip = 0)
    {
        MapDefinition map = this.Map(mapId);
        int startX = (int)(map.SpawnX / 16);
        int startY = (int)(map.SpawnY / 16);
        List<(int X, int Y)> found = [];
        for (int radius = 0; radius < 60; radius++)
        {
            for (int y = startY - radius; y <= startY + radius; y++)
            {
                for (int x = startX - radius; x <= startX + radius; x++)
                {
                    bool open = true;
                    for (int dy = 0; dy <= height && open; dy++)
                    {
                        for (int dx = 0; dx < width && open; dx++)
                        {
                            open = x + dx >= 0 && y + dy >= 0 && x + dx < map.Grid.Width && y + dy < map.Grid.Height && !map.Grid.IsBlocked(x + dx, y + dy) && !map.ReservedTiles.Contains((x + dx, y + dy));
                        }
                    }

                    if (open && !found.Any(f => Math.Abs(f.X - x) < width + 1 && Math.Abs(f.Y - y) < height + 2))
                    {
                        found.Add((x, y));
                        if (found.Count > skip)
                        {
                            return found[skip];
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException("No open area on " + mapId);
    }

    // Stands a player in front of the carpenter's counter in Town.
    public void StandAtCarpenter(Guid player)
    {
        MapSpec town = GameMaps.All.Single(m => m.Id == GameMaps.Town);
        ShopPoint counter = town.Shops.Single(p => p.ShopId == Nosum.Gameplay.Shops.ShopCatalog.CarpenterShop);
        this.Presence.Stand(player, MapInstance.Shared(GameMaps.Town), (counter.TileX * 16) + 8, (counter.TileY * 16) + 8);
    }

    public async Task GiveCoinsAsync(Guid player, long coins)
    {
        TransferStatus status = await this.Transfers.ExecuteAsync([new TransferLeg(TransferParty.Player(player), 0, 0, coins)], "test.setup");
        if (status != TransferStatus.Ok)
        {
            throw new InvalidOperationException("Granting coins failed: " + status);
        }
    }

    public Task<long> CoinsOfAsync(Guid player) => this.Transfers.GetBalanceAsync(player);

    // Commissions a building at the counter, places it on a free area of the player's farm and waits (by moving the clock) until it is finished; the player ends up standing at the counter again.
    public async Task<BuildingInfo> BuildAsync(Guid owner, Nosum.Gameplay.Buildings.BuildingKind kind, int skip = 0)
    {
        Nosum.Gameplay.Buildings.BuildingDefinition definition = Nosum.Gameplay.Buildings.BuildingCatalog.All.Single(b => b.Kind == kind);
        await this.GiveCoinsAsync(owner, definition.Coins);
        _ = await this.Inventory.AddAsync(owner, ItemCatalog.Wood, definition.Wood, "test.setup");
        _ = await this.Inventory.AddAsync(owner, ItemCatalog.Stone, definition.Stone, "test.setup");
        this.StandAtCarpenter(owner);
        BuildingOutcome commissioned = await this.Buildings.CommissionAsync(this.Presence.ConnectionOf(owner), kind);
        if (commissioned.Status != Nosum.Contracts.Building.BuildingStatus.Ok)
        {
            throw new InvalidOperationException("Commissioning failed: " + commissioned.Status);
        }

        (int x, int y) = this.OpenArea(GameMaps.Farm, definition.View.FootprintWidth, definition.View.FootprintHeight, skip);
        this.StandOnFarm(owner, x + definition.DoorX, y + definition.View.FootprintHeight);
        int slot = (await this.Inventory.GetAsync(owner)).Slots.First(s => s.ItemId == definition.BlueprintItemId).Slot;
        Nosum.Abstractions.Placeables.PlaceOutcome placed = await this.Placeables.PlaceAsync(this.Presence.ConnectionOf(owner), slot, x, y, 0);
        if (placed.Status != PlaceStatus.Ok)
        {
            throw new InvalidOperationException("Placing the blueprint failed: " + placed.Status);
        }

        this.Clock.Advance(TimeSpan.FromMilliseconds(Nosum.Gameplay.Buildings.BuildingCatalog.ConstructionDays * this.Calendar.DayLengthMs));
        MapInstance land = new(GameMaps.Farm, owner);
        BuildingInfo info = this.BuildingDirectory.BuildingsOf(land).Single(b => b.ObjectId == placed.ObjectId);
        this.StandAtCarpenter(owner);
        return info;
    }

    // Stands a player inside a building, at a tile of its interior.
    public void StandInside(Guid player, BuildingInfo building, int tileX, int tileY) => this.Presence.Stand(player, building.Interior, (tileX * 16) + 8, (tileY * 16) + 8);

    // Stands a player right next to an animal (feet on its feet).
    public void StandAt(Guid player, BuildingInfo building, Guid animal)
    {
        AnimalMotionView where = this.Wander.Where(building.Kind, animal, this.Clock.GetUtcNow().ToUnixTimeMilliseconds());
        this.Presence.Stand(player, building.Interior, where.X, where.Y);
    }

    // Puts a player on a tile of their own farm (feet in the middle of the tile).
    public void StandOnFarm(Guid player, int tileX, int tileY) => this.Presence.Stand(player, new MapInstance(GameMaps.Farm, player), (tileX * 16) + 8, (tileY * 16) + 8);

    public void Stand(Guid player, MapInstance instance, int tileX, int tileY) => this.Presence.Stand(player, instance, (tileX * 16) + 8, (tileY * 16) + 8);

    // Gives a player items (a dev grant), returns the first slot that now holds them.
    public async Task<int> GiveAsync(Guid player, int itemId, int quantity)
    {
        _ = await this.Inventory.AddAsync(player, itemId, quantity, "test.setup");
        return (await this.Inventory.GetAsync(player)).Slots.First(s => s.ItemId == itemId).Slot;
    }

    public async Task<int> CountAsync(Guid player, int itemId)
        => (await this.Inventory.GetAsync(player)).Slots.Where(s => s.ItemId == itemId).Sum(s => s.Quantity);

    public async Task<int> CountInChestAsync(int objectId, int itemId)
        => (await this.Inventory.GetAsync(Nosum.Gameplay.Inventory.ContainerIds.ForChest(objectId))).Slots.Where(s => s.ItemId == itemId).Sum(s => s.Quantity);

    // Gives the player the item, places it on a tile of the place they stand in (their farm, co-op land, the farmhouse), returns the new object's id.
    public async Task<int> PlaceOnFarmAsync(Guid owner, int itemId, int tileX, int tileY)
    {
        int slot = await this.GiveAsync(owner, itemId, 1);
        Nosum.Abstractions.Placeables.PlaceOutcome outcome = await this.Placeables.PlaceAsync(this.Presence.ConnectionOf(owner), slot, tileX, tileY, 0);
        if (outcome.Status != PlaceStatus.Ok)
        {
            throw new InvalidOperationException("Placing failed: " + outcome.Status);
        }

        return outcome.ObjectId;
    }

    // A co-op with the given players as members (the first is the founder); returns its id.
    public async Task<Guid> CreateCoopAsync(params Guid[] members)
    {
        await using NosumDbContext context = this.Db();
        Coop coop = new() { Name = "Shared fields", CreatedAt = Start };
        context.Coops.Add(coop);
        for (int i = 0; i < members.Length; i++)
        {
            context.CoopMembers.Add(new CoopMember { CoopId = coop.Id, PlayerId = members[i], Role = i == 0 ? CoopRole.Founder : CoopRole.Member, JoinedAt = Start });
        }

        _ = await context.SaveChangesAsync();
        return coop.Id;
    }

    public void Dispose()
    {
    }

    /// <summary>Skill levels a test chooses.</summary>
    public sealed class FixedLevels : ISkillLevels
    {
        private readonly Func<CraftSkill, int> _levels;

        public FixedLevels(Func<CraftSkill, int> levels) => _levels = levels;

        public ValueTask<int> LevelOfAsync(Guid playerId, CraftSkill skill, CancellationToken cancellationToken = default) => ValueTask.FromResult(_levels(skill));
    }

    /// <summary>Nodes at chosen tiles.</summary>
    public sealed class NodesAt : INodeTiles
    {
        private readonly HashSet<(int X, int Y)> _tiles;

        public NodesAt(params (int X, int Y)[] tiles) => _tiles = [.. tiles];

        public Task<IReadOnlySet<(int X, int Y)>> NodeTilesAsync(MapInstance instance, int tileX, int tileY, int width, int height, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<(int X, int Y)>>(_tiles.Where(t => t.X >= tileX && t.X < tileX + width && t.Y >= tileY && t.Y < tileY + height).ToHashSet());
    }

    /// <summary>Tilled soil at chosen tiles.</summary>
    public sealed class TilledAt : IFarmTiles
    {
        private readonly HashSet<(int X, int Y)> _tiles;

        public TilledAt(params (int X, int Y)[] tiles) => _tiles = [.. tiles];

        public Task<IReadOnlySet<(int X, int Y)>> WorkedTilesAsync(MapInstance instance, int tileX, int tileY, int width, int height, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlySet<(int X, int Y)>>(_tiles.Where(t => t.X >= tileX && t.X < tileX + width && t.Y >= tileY && t.Y < tileY + height).ToHashSet());
    }
}

/// <summary>A connection nobody talks to: presence only stores it.</summary>
internal class StubConnection : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        => targetMethod is { ReturnType: { IsValueType: true } type } && type != typeof(void) ? Activator.CreateInstance(type) : null;
}

/// <summary>
/// Reports each registered player at a chosen place of a chosen instance and records the packets the server sent: <c>SendAsync</c> to one connection,
/// <c>MulticastAsync</c> to an instance. Only the packet types the placeable services use are copied; the pooled packet is back in its pool when the call returns.
/// </summary>
internal sealed class StandingPresence : IMapPresence
{
    private readonly Dictionary<Guid, (IConnection Connection, PlayerPresence Presence)> _byPlayer = [];
    private readonly Dictionary<IConnection, PlayerPresence> _byConnection = [];
    private readonly List<(Guid? Account, object Packet)> _sent = [];

    public event Func<PlayerPresence, ValueTask>? PlayerLeft;

    public int PlayerCount => _byPlayer.Count;

    public IConnection ConnectionOf(Guid player) => _byPlayer[player].Connection;

    [SuppressMessage("Reliability", "CA2000", Justification = "A stub connection that holds nothing to dispose.")]
    public void Stand(Guid account, MapInstance instance, double x, double y)
    {
        lock (_byPlayer)
        {
            IConnection connection = _byPlayer.TryGetValue(account, out (IConnection Connection, PlayerPresence Presence) known) ? known.Connection : DispatchProxy.Create<IConnection, StubConnection>();
            PlayerPresence presence = new(1, account, instance, connection, new MotionState(x, y, 0));
            _byPlayer[account] = (connection, presence);
            _byConnection[connection] = presence;
        }
    }

    // The player walks away: they are no longer anywhere, and the leave event runs.
    public async Task LeaveAsync(Guid account)
    {
        PlayerPresence presence;
        lock (_byPlayer)
        {
            presence = _byPlayer[account].Presence;
            _ = _byPlayer.Remove(account);
            _ = _byConnection.Remove(presence.Connection);
        }

        if (this.PlayerLeft is { } handler)
        {
            await handler(presence);
        }
    }

    public IReadOnlyList<T> Sent<T>(Guid? account = null)
    {
        lock (_sent)
        {
            return [.. _sent.Where(s => s.Packet is T && (account is null || s.Account == account)).Select(s => (T)s.Packet)];
        }
    }

    public ValueTask<PlayerPresence> JoinAsync(IConnection connection, Guid accountId, MapInstance instance, MotionState motion, uint? playerId = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public ValueTask LeaveAsync(IConnection connection, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public bool TryGet(IConnection connection, [NotNullWhen(true)] out PlayerPresence? player)
    {
        lock (_byPlayer)
        {
            return _byConnection.TryGetValue(connection, out player);
        }
    }

    public void CopyActiveMapIds(List<string> into)
    {
        lock (_byPlayer)
        {
            into.AddRange(_byPlayer.Values.Select(v => v.Presence.Key).Distinct(StringComparer.Ordinal));
        }
    }

    public void CopyPlayers(string instanceKey, List<PlayerPresence> into)
    {
        lock (_byPlayer)
        {
            into.AddRange(_byPlayer.Values.Select(v => v.Presence).Where(p => p.Key == instanceKey));
        }
    }

    public ValueTask SendAsync(IConnection connection, IPacket packet, CancellationToken cancellationToken = default)
    {
        Guid? account = null;
        lock (_byPlayer)
        {
            if (_byConnection.TryGetValue(connection, out PlayerPresence? presence))
            {
                account = presence.AccountId;
            }
        }

        this.Record(account, packet);
        return ValueTask.CompletedTask;
    }

    public ValueTask MulticastAsync(string instanceKey, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default)
    {
        this.Record(null, packet);
        return ValueTask.CompletedTask;
    }

    // The tiles are copied out of the pooled packet, like the other copies below.
    private static FarmTilesChanged CopyFarmTiles(FarmTilesChanged packet)
    {
        FarmTilesChanged copy = new() { MapId = packet.MapId, ServerNowMs = packet.ServerNowMs };
        foreach (FarmTileRow row in packet.Tiles)
        {
            copy.Tiles.Add(new FarmTileRow { X = row.X, Y = row.Y, CropId = row.CropId, PlantedAtMs = row.PlantedAtMs, GrowthDays = row.GrowthDays, WateredDay = row.WateredDay, TendedDay = row.TendedDay, Removed = row.Removed });
        }

        return copy;
    }

    [SuppressMessage("Reliability", "CA2000", Justification = "The copies are plain data kept for the test to read; their pooled buffers are never rented.")]
    private void Record(Guid? account, IPacket packet)
    {
        object? copy = packet switch
        {
            ChestUpdate u => new ChestUpdate { ObjectId = u.ObjectId, Slots = [.. u.Slots.Select(s => new InventorySlotRow { Slot = s.Slot, ItemId = s.ItemId, Quantity = s.Quantity })] },
            ChestClosed c => new ChestClosed { ObjectId = c.ObjectId },
            AnimalsChanged a => new AnimalsChanged { BuildingId = a.BuildingId, Rations = a.Rations, Upserted = [.. a.Upserted.Select(r => new AnimalRow { Id = r.Id, Kind = r.Kind, Name = r.Name, Friendship = r.Friendship, Flags = r.Flags, X = r.X, Y = r.Y, TargetX = r.TargetX, TargetY = r.TargetY, MoveMs = r.MoveMs })], Removed = [.. a.Removed] },
            AnimalsMoved m => new AnimalsMoved { BuildingId = m.BuildingId, Moves = [.. m.Moves.Select(r => new AnimalMoveRow { Id = r.Id, FromX = r.FromX, FromY = r.FromY, ToX = r.ToX, ToY = r.ToY, DurationMs = r.DurationMs })] },
            BuildingsSnapshot b => new BuildingsSnapshot { Mine = b.Mine, Buildings = [.. b.Buildings.Select(r => new BuildingRow { Id = r.Id, ObjectId = r.ObjectId, Kind = r.Kind, MapId = r.MapId, RemainingMs = r.RemainingMs, Animals = r.Animals, Capacity = r.Capacity })] },
            FurnaceChanged f => new FurnaceChanged { ObjectId = f.ObjectId, State = f.State, OreItemId = f.OreItemId, RemainingMs = f.RemainingMs },
            PlacedChanged p => new PlacedChanged { MapId = p.MapId, Upserted = [.. p.Upserted.Select(r => new PlacedObjectRow { Id = r.Id, ItemId = r.ItemId, TileX = r.TileX, TileY = r.TileY, Rotation = r.Rotation, PlacedAtMs = r.PlacedAtMs })], Removed = [.. p.Removed] },
            FarmTilesChanged f => CopyFarmTiles(f),
            _ => null,
        };
        if (copy is not null)
        {
            lock (_sent)
            {
                _sent.Add((account, copy));
            }
        }
    }
}

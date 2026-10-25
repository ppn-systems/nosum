// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.GroundItems;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.GroundItem;
using Nosum.Contracts.GroundItem.S2C;
using Nosum.Domain.Accounts;
using Nosum.Domain.World;
using Nosum.Gameplay.GroundItems;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.GroundItems;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Items lying on the ground against a real PostgreSQL database: dropping, automatic pickup through the atomic transfer, the race for one item,
/// a full inventory, the owner's priority window, expiry and surviving a restart (docs/adr/ADR-021 D5-D6).
/// </summary>
public sealed class GroundItemServiceTests : IClassFixture<PostgresFixture>
{
    private const int Tile = 16;

    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly EfUnitOfWorkFactory _units;
    private readonly InventoryService _inventory;
    private readonly TransferService _transfers;
    private readonly RecordingPresence _presence = new();
    private readonly GroundItemService _items;

    public GroundItemServiceTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _inventory = new InventoryService(_units, _clock);
        _transfers = new TransferService(_inventory, _clock);
        _items = this.NewService();
    }

    [Fact]
    public async Task Drop_OnASharedMap_PersistsTheRowWithItsTimingAndTellsTheInstance()
    {
        Guid cause = await CreatePlayerAsync();
        MapInstance town = Unique();

        int stacks = await _items.DropAsync(new GroundDrop(town, 100, 120, ItemCatalog.Parsnip, 3, cause));

        stacks.Should().Be(1);
        GroundItem row = (await RowsAsync(town)).Should().ContainSingle().Which;
        (row.ItemId, row.Quantity, row.X, row.Y, row.CausedBy).Should().Be((ItemCatalog.Parsnip, 3, 100, 120, cause));
        row.PriorityUntilMs.Should().Be(s_start.ToUnixTimeMilliseconds() + GroundItemRules.OwnerPriorityMs);
        row.ExpiresAtMs.Should().Be(s_start.ToUnixTimeMilliseconds() + GroundItemRules.SharedExpiryMs);
        GroundItemSpawned spawned = _presence.Sent.OfType<GroundItemSpawned>().Should().ContainSingle().Which;
        (spawned.MapId, spawned.Item.Id, spawned.Item.ItemId, spawned.Item.Quantity, spawned.Item.X, spawned.Item.Y).Should().Be((town.MapId, row.Id, ItemCatalog.Parsnip, 3, (ushort)100, (ushort)120));
    }

    [Fact]
    public async Task Drop_OnAPrivateInstance_HasNoPriorityWindowAndNeverExpires()
    {
        Guid owner = await CreatePlayerAsync();
        MapInstance farm = new(GameMaps.Farm, owner);

        _ = await _items.DropAsync(new GroundDrop(farm, 50, 50, ItemCatalog.Radish, 1, owner));

        GroundItem row = (await RowsAsync(farm)).Should().ContainSingle().Which;
        (row.PriorityUntilMs, row.ExpiresAtMs).Should().Be((0, 0));
    }

    [Theory]
    [InlineData(4242, 1, 100, 100)]
    [InlineData(ItemCatalog.Parsnip, 0, 100, 100)]
    [InlineData(ItemCatalog.Parsnip, -3, 100, 100)]
    [InlineData(ItemCatalog.Parsnip, 1, -1, 100)]
    [InlineData(ItemCatalog.Parsnip, 1, 100, 110 * Tile)]
    [InlineData(ItemCatalog.Parsnip, 1, 130 * Tile, 100)]
    [InlineData(ItemCatalog.Parsnip, 5000, 100, 100)]
    public async Task Drop_InvalidRequest_DropsNothing(int itemId, int quantity, int x, int y)
    {
        MapInstance town = Unique();

        (await _items.DropAsync(new GroundDrop(town, x, y, itemId, quantity, null))).Should().Be(0);

        (await RowsAsync(town)).Should().BeEmpty();
        _presence.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Drop_MoreThanOneStack_SplitsIntoStacksOfAtMostTheStackSize()
    {
        MapInstance town = Unique();
        int stack = ItemCatalog.TryGet(ItemCatalog.Parsnip, out ItemDefinition? parsnip) ? parsnip.MaxStack : 0;

        (await _items.DropAsync(new GroundDrop(town, 200, 200, ItemCatalog.Parsnip, (stack * 2) + 5, null))).Should().Be(3);

        (await RowsAsync(town)).Select(r => r.Quantity).Should().BeEquivalentTo([stack, stack, 5]);
    }

    [Fact]
    public async Task OnPlayerMoved_ItemWithinReach_GoesIntoTheInventoryAndTheRowIsDeletedInOneTransfer()
    {
        Guid player = await CreatePlayerAsync();
        MapInstance town = Unique();
        _ = await _items.DropAsync(new GroundDrop(town, 100, 100, ItemCatalog.Parsnip, 4, null));
        _presence.Sent.Clear();

        PlayerPresence presence = Stand(player, town, 100 + 20, 100);
        await _items.OnPlayerMovedAsync(presence);

        (await CountAsync(player, ItemCatalog.Parsnip)).Should().Be(4);
        (await RowsAsync(town)).Should().BeEmpty();
        GroundItemTaken taken = _presence.Sent.OfType<GroundItemTaken>().Should().ContainSingle().Which;
        (taken.Reason, taken.ToX, taken.ToY).Should().Be((GroundItemGone.PickedUp, (ushort)120, (ushort)100));
        await using NosumDbContext context = new(_database.Options);
        (await context.InventoryLog.Where(e => e.PlayerId == player && e.Reason == "ground.pickup").ToListAsync()).Should().ContainSingle().Which.Delta.Should().Be(4);
        (await _items.GetAsync(presence.Connection)).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task OnPlayerMoved_ItemOutOfReach_LeavesItAlone()
    {
        Guid player = await CreatePlayerAsync();
        MapInstance town = Unique();
        _ = await _items.DropAsync(new GroundDrop(town, 100, 100, ItemCatalog.Parsnip, 1, null));

        await _items.OnPlayerMovedAsync(Stand(player, town, 100 + 25, 100));
        await _items.OnPlayerMovedAsync(Stand(player, town, 100, 100 + 40));

        (await CountAsync(player, ItemCatalog.Parsnip)).Should().Be(0);
        (await RowsAsync(town)).Should().ContainSingle();
    }

    [Fact]
    public async Task OnPlayerMoved_ItemAcrossAChunkBorderButWithinReach_IsPickedUp()
    {
        Guid player = await CreatePlayerAsync();
        MapInstance town = Unique();

        // x = 256 is the first pixel of chunk 1; the player stands in chunk 0, 10 px away.
        _ = await _items.DropAsync(new GroundDrop(town, 256, 100, ItemCatalog.Parsnip, 1, null));
        await _items.OnPlayerMovedAsync(Stand(player, town, 246, 100));

        (await CountAsync(player, ItemCatalog.Parsnip)).Should().Be(1);
    }

    [Fact]
    public async Task OnPlayerMoved_TwoPlayersRaceForOneItem_ExactlyOneGetsIt()
    {
        Guid first = await CreatePlayerAsync();
        Guid second = await CreatePlayerAsync();
        MapInstance town = Unique();
        PlayerPresence a = Stand(first, town, 300, 300);
        PlayerPresence b = Stand(second, town, 305, 300);

        // Many rounds: every round drops one item and both players move at the same moment.
        const int rounds = 25;
        for (int i = 0; i < rounds; i++)
        {
            _ = await _items.DropAsync(new GroundDrop(town, 302, 300, ItemCatalog.Parsnip, 1, null));
            await Task.WhenAll(_items.OnPlayerMovedAsync(a), _items.OnPlayerMovedAsync(b));
        }

        int total = await CountAsync(first, ItemCatalog.Parsnip) + await CountAsync(second, ItemCatalog.Parsnip);
        total.Should().Be(rounds, "each item is taken exactly once, never by both and never lost");
        (await RowsAsync(town)).Should().BeEmpty();
        _presence.Sent.OfType<GroundItemTaken>().Select(t => t.Id).Distinct().Should().HaveCount(rounds);
    }

    [Fact]
    public async Task OnPlayerMoved_FullInventory_LeavesTheItemAndTakesItOnceThereIsRoom()
    {
        Guid player = await CreatePlayerAsync();
        MapInstance town = Unique();
        _ = await _inventory.AddAsync(player, ItemCatalog.ShirtRed, InventoryLimits.DefaultCapacity - StarterKit.Items.Length, "test.fill"); // the starter kit plus this fills the bag
        _ = await _items.DropAsync(new GroundDrop(town, 100, 100, ItemCatalog.Parsnip, 1, null));
        PlayerPresence presence = Stand(player, town, 100, 100);

        await _items.OnPlayerMovedAsync(presence);

        (await CountAsync(player, ItemCatalog.Parsnip)).Should().Be(0);
        (await RowsAsync(town)).Should().ContainSingle("the item stays where it lay");

        // The player makes room; once the short retry pause has passed the next move takes it.
        await using (NosumDbContext context = new(_database.Options))
        {
            _ = await context.InventorySlots.Where(s => s.PlayerId == player && s.ItemId == ItemCatalog.ShirtRed).ExecuteDeleteAsync();
        }

        _clock.Advance(TimeSpan.FromSeconds(3));
        await _items.OnPlayerMovedAsync(presence);

        (await CountAsync(player, ItemCatalog.Parsnip)).Should().Be(1);
        (await RowsAsync(town)).Should().BeEmpty();
    }

    [Fact]
    public async Task OnPlayerMoved_WithinTheOwnersPriorityWindow_OnlyTheCauseTakesIt()
    {
        Guid cause = await CreatePlayerAsync();
        Guid other = await CreatePlayerAsync();
        MapInstance town = Unique();
        _ = await _items.DropAsync(new GroundDrop(town, 100, 100, ItemCatalog.Parsnip, 1, cause));
        PlayerPresence stranger = Stand(other, town, 100, 100);

        await _items.OnPlayerMovedAsync(stranger);
        (await CountAsync(other, ItemCatalog.Parsnip)).Should().Be(0);

        _clock.Advance(TimeSpan.FromMilliseconds(GroundItemRules.OwnerPriorityMs - 1));
        await _items.OnPlayerMovedAsync(stranger);
        (await CountAsync(other, ItemCatalog.Parsnip)).Should().Be(0, "the window lasts the full five seconds");

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        await _items.OnPlayerMovedAsync(stranger);
        (await CountAsync(other, ItemCatalog.Parsnip)).Should().Be(1, "then anyone may take it");
    }

    [Fact]
    public async Task OnPlayerMoved_TheCauseDuringTheWindow_TakesIt()
    {
        Guid cause = await CreatePlayerAsync();
        MapInstance town = Unique();
        _ = await _items.DropAsync(new GroundDrop(town, 100, 100, ItemCatalog.Parsnip, 2, cause));

        await _items.OnPlayerMovedAsync(Stand(cause, town, 110, 100));

        (await CountAsync(cause, ItemCatalog.Parsnip)).Should().Be(2);
    }

    [Fact]
    public async Task OnPlayerMoved_OnAPrivateFarm_OnlyTheOwnerAndTheCauseTakeIt()
    {
        Guid owner = await CreatePlayerAsync();
        Guid visitor = await CreatePlayerAsync();
        Guid helper = await CreatePlayerAsync();
        MapInstance farm = new(GameMaps.Farm, owner);
        _ = await _items.DropAsync(new GroundDrop(farm, 100, 100, ItemCatalog.Parsnip, 1, helper));
        _ = await _items.DropAsync(new GroundDrop(farm, 100, 100, ItemCatalog.Radish, 1, null));

        // Long after any priority window: a visitor still cannot walk off with the harvest.
        _clock.Advance(TimeSpan.FromHours(2));
        await _items.OnPlayerMovedAsync(Stand(visitor, farm, 100, 100));
        (await CountAsync(visitor, ItemCatalog.Parsnip) + await CountAsync(visitor, ItemCatalog.Radish)).Should().Be(0);

        await _items.OnPlayerMovedAsync(Stand(helper, farm, 100, 100));
        await _items.OnPlayerMovedAsync(Stand(owner, farm, 100, 100));

        (await CountAsync(helper, ItemCatalog.Parsnip)).Should().Be(1, "whoever caused the drop may take it");
        (await CountAsync(owner, ItemCatalog.Radish)).Should().Be(1, "the owner may take what lies on their farm");
        (await RowsAsync(farm)).Should().BeEmpty();
    }

    [Fact]
    public async Task ExpireDue_ItemsOnSharedMapsVanishAfterTenMinutesButPrivateOnesStay()
    {
        Guid owner = await CreatePlayerAsync();
        MapInstance town = Unique();
        MapInstance farm = new(GameMaps.Farm, owner);
        _ = await _items.DropAsync(new GroundDrop(town, 100, 100, ItemCatalog.Parsnip, 1, null));
        _ = await _items.DropAsync(new GroundDrop(farm, 100, 100, ItemCatalog.Parsnip, 1, owner));
        _presence.Sent.Clear();

        _clock.Advance(TimeSpan.FromMilliseconds(GroundItemRules.SharedExpiryMs - 1));
        (await _items.ExpireDueAsync()).Should().Be(0);
        (await RowsAsync(town)).Should().ContainSingle();

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        (await _items.ExpireDueAsync()).Should().Be(1);

        (await RowsAsync(town)).Should().BeEmpty();
        (await RowsAsync(farm)).Should().ContainSingle("a private farm's items never expire");
        _presence.Sent.OfType<GroundItemTaken>().Should().ContainSingle().Which.Reason.Should().Be(GroundItemGone.Expired);
        (await _items.ExpireDueAsync()).Should().Be(0);
    }

    [Fact]
    public async Task OnPlayerMoved_ExpiredButNotYetSweptItem_CannotBePickedUp()
    {
        Guid player = await CreatePlayerAsync();
        MapInstance town = Unique();
        _ = await _items.DropAsync(new GroundDrop(town, 100, 100, ItemCatalog.Parsnip, 1, null));

        _clock.Advance(TimeSpan.FromMilliseconds(GroundItemRules.SharedExpiryMs));
        await _items.OnPlayerMovedAsync(Stand(player, town, 100, 100));

        (await CountAsync(player, ItemCatalog.Parsnip)).Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_AfterARestart_ReturnsThePersistedItemsAndTheyCanBePickedUp()
    {
        Guid player = await CreatePlayerAsync();
        MapInstance town = Unique();
        _ = await _items.DropAsync(new GroundDrop(town, 100, 100, ItemCatalog.Cauliflower, 2, null));

        // A new service over the same database: what the crashed process left on the ground is still there.
        GroundItemService restarted = this.NewService();
        PlayerPresence presence = Stand(player, town, 100, 100);
        GroundItemsView view = await restarted.GetAsync(presence.Connection);

        view.MapId.Should().Be(town.MapId);
        view.Items.Should().ContainSingle().Which.Should().Match<GroundItemView>(i => i.ItemId == ItemCatalog.Cauliflower && i.Quantity == 2 && i.X == 100 && i.Y == 100);
        await restarted.OnPlayerMovedAsync(presence);
        (await CountAsync(player, ItemCatalog.Cauliflower)).Should().Be(2);
    }

    [Fact]
    public async Task Stage_JoinsTheCallersTransaction_NothingExistsUntilItSavesAndIsCompleted()
    {
        MapInstance town = Unique();
        IUnitOfWork unit = _units.Create();
        await using (unit)
        {
            PendingGroundDrop pending = _items.Stage(unit, new GroundDrop(town, 100, 100, ItemCatalog.Radish, 1, null))!;
            (await RowsAsync(town)).Should().BeEmpty("the caller has not saved yet");
            _presence.Sent.Should().BeEmpty();

            _ = await unit.SaveChangesAsync(CancellationToken.None);
            await _items.CompleteAsync(pending);

            pending.Rows.Single().Id.Should().BeGreaterThan(0);
        }

        (await RowsAsync(town)).Should().ContainSingle();
        _presence.Sent.OfType<GroundItemSpawned>().Should().ContainSingle();
    }

    [Fact]
    public async Task OnPlayerMoved_NothingLyingNearby_DoesNotTouchTheDatabase()
    {
        Guid player = await CreatePlayerAsync();
        MapInstance town = Unique();
        _ = await _items.DropAsync(new GroundDrop(town, 2000, 1500, ItemCatalog.Parsnip, 1, null));
        _presence.Sent.Clear();

        // The only item is many chunks away; the move completes at once and nothing is sent.
        Task moved = _items.OnPlayerMovedAsync(Stand(player, town, 100, 100));

        moved.IsCompletedSuccessfully.Should().BeTrue();
        _presence.Sent.Should().BeEmpty();
    }

    private static MapInstance Unique() => MapInstance.Shared(GameMaps.Town + "-" + Guid.NewGuid().ToString("N")[..8]);

    [SuppressMessage("Reliability", "CA2000", Justification = "A stub connection that holds nothing to dispose.")]
    private PlayerPresence Stand(Guid account, MapInstance instance, double x, double y)
        => _presence.Register(new PlayerPresence(1, account, instance, DispatchProxy.Create<IConnection, ConnectionStub>(), new MotionState(x, y, 0)));

    private GroundItemService NewService() => new(_presence, new BlankCatalog(), _transfers, _units, _clock);

    private async Task<List<GroundItem>> RowsAsync(MapInstance instance)
    {
        await using NosumDbContext context = new(_database.Options);
        return await context.GroundItems.Where(g => g.MapId == instance.MapId && g.OwnerId == instance.OwnerId).ToListAsync();
    }

    private async Task<int> CountAsync(Guid player, int itemId)
        => (await _inventory.GetAsync(player)).Slots.Where(s => s.ItemId == itemId).Sum(s => s.Quantity);

    private async Task<Guid> CreatePlayerAsync()
    {
        IUnitOfWork unit = _units.Create();
        await using (unit)
        {
            Player player = TestNicknames.Name(new() { IsGuest = true, CreatedAt = s_start, LastActiveAt = s_start });
            unit.Repository<Player>().Add(player);
            _ = await unit.SaveChangesAsync(CancellationToken.None);
            return player.Id;
        }
    }

    // A connection nobody talks to: presence only stores it.
    private class ConnectionStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod is { ReturnType: { IsValueType: true } type } && type != typeof(void) ? Activator.CreateInstance(type) : null;
    }

    // Every map is a blank 130 x 110 grid of 16 px tiles.
    private sealed class BlankCatalog : IMapCatalog
    {
        public bool TryGet(string mapId, [NotNullWhen(true)] out MapDefinition? map)
        {
            map = new MapDefinition(mapId, new WalkGrid(130, 110, Tile, new bool[130 * 110]), 0, 0);
            return true;
        }
    }

    // Remembers what the service multicast and who stands where; everything else is empty.
    private sealed class RecordingPresence : IMapPresence
    {
        private readonly Dictionary<IConnection, PlayerPresence> _players = [];

        public List<IPacket> Sent { get; } = [];

        public PlayerPresence Register(PlayerPresence player)
        {
            _players[player.Connection] = player;
            return player;
        }

        public event Func<PlayerPresence, ValueTask>? PlayerLeft
        {
            add { }
            remove { }
        }

        public int PlayerCount => 0;

        public ValueTask<PlayerPresence> JoinAsync(IConnection connection, Guid accountId, MapInstance instance, MotionState motion, uint? playerId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask LeaveAsync(IConnection connection, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public bool TryGet(IConnection connection, [NotNullWhen(true)] out PlayerPresence? player)
        {
            return _players.TryGetValue(connection, out player);
        }

        public void CopyActiveMapIds(List<string> into)
        {
        }

        public void CopyPlayers(string instanceKey, List<PlayerPresence> into)
        {
        }

        public ValueTask SendAsync(IConnection connection, IPacket packet, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask MulticastAsync(string instanceKey, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default)
        {
            // Packets are pooled and returned right after the call: keep a copy of what the tests look at.
            lock (this.Sent)
            {
                this.Sent.Add(Copy(packet));
            }

            return ValueTask.CompletedTask;
        }

        private static IPacket Copy(IPacket packet) => packet switch
        {
            GroundItemSpawned s => new GroundItemSpawned { MapId = s.MapId, Item = new GroundItemRow { Id = s.Item.Id, ItemId = s.Item.ItemId, Quantity = s.Item.Quantity, X = s.Item.X, Y = s.Item.Y } },
            GroundItemTaken t => new GroundItemTaken { MapId = t.MapId, Id = t.Id, Reason = t.Reason, ToX = t.ToX, ToY = t.ToY },
            _ => packet,
        };
    }
}

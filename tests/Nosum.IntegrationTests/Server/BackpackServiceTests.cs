// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Inventory;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Placeables;
using Nosum.Gameplay.Shops;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The tailor's backpack upgrades (docs/adr/ADR-081) against a real PostgreSQL database: the coins and the new size move together or not at all, the size
/// survives a cache that was forgotten, two purchases of one size cannot both succeed, and a chest is never grown.
/// </summary>
public sealed class BackpackServiceTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly EfUnitOfWorkFactory _units;
    private readonly InventoryService _inventory;
    private readonly TransferService _transfers;
    private readonly StandingPresence _presence = new();
    private readonly BackpackService _backpack;
    private readonly IConnection _connection = DispatchProxy.Create<IConnection, ConnectionStub>();

    public BackpackServiceTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _inventory = new InventoryService(_units, _clock);
        _transfers = new TransferService(_inventory, _clock);
        _backpack = new BackpackService(_transfers, _inventory, _presence, new TownCatalog());
    }

    [Fact]
    public async Task Upgrade_AtTheTailor_TakesSixHundredCoinsAndGrowsTheBag_AndTheSizeIsStoredAfterTheCacheIsForgotten()
    {
        Guid player = await CreatePlayerAsync();
        StandAtCounter(player, ShopCatalog.ClothingShop);
        await GiveCoinsAsync(player, 100); // 500 + 100 = 600

        BackpackView view = await _backpack.UpgradeAsync(_connection);

        view.Should().Be(new BackpackView(BackpackStatus.Ok, 45, 54, 1_000));
        (await _transfers.GetBalanceAsync(player)).Should().Be(0);
        _inventory.Forget(player);
        (await _inventory.GetAsync(player)).Capacity.Should().Be(45, "the size is saved, not only held in memory");
        await using NosumDbContext context = new(_database.Options);
        (await context.InventoryLog.Where(e => e.PlayerId == player && e.Reason == "backpack.upgrade").ToListAsync()).Should().BeEmpty("a growth moves no items and writes no item audit line");
        (await context.CoinLog.Where(e => e.PlayerId == player && e.Reason == "backpack.upgrade").ToListAsync()).Should().ContainSingle().Which.Delta.Should().Be(-600);
    }

    [Fact]
    public async Task Upgrade_TheGrownSlotsAreUsable_AndReadBackAfterTheCacheIsForgotten()
    {
        Guid player = await CreatePlayerAsync();
        StandAtCounter(player, ShopCatalog.ClothingShop);
        await GiveCoinsAsync(player, 100);
        (await _backpack.UpgradeAsync(_connection)).Status.Should().Be(BackpackStatus.Ok);

        InventoryChange placed = await _inventory.ApplyAsync(player, [InventoryOperation.AddToSlot(40, ItemCatalog.ShirtRed, 1)], "test.place");

        placed.Status.Should().Be(InventoryStatus.Ok, "slot 40 exists only in the bigger bag");
        _inventory.Forget(player);
        InventorySlotView slot = await _inventory.PeekSlotAsync(player, 40);
        slot.Should().Be(new InventorySlotView(40, ItemCatalog.ShirtRed, 1));
    }

    [Fact]
    public async Task Upgrade_NotAtTheCounter_IsRefused_AndNothingIsCharged()
    {
        Guid player = await CreatePlayerAsync();
        StandAtCounter(player, ShopCatalog.SeedShop);
        await GiveCoinsAsync(player, 100);

        BackpackView view = await _backpack.UpgradeAsync(_connection);

        view.Status.Should().Be(BackpackStatus.NotAtCounter);
        (await _transfers.GetBalanceAsync(player)).Should().Be(600);
        (await _inventory.GetAsync(player)).Capacity.Should().Be(InventoryLimits.DefaultCapacity);
    }

    [Fact]
    public async Task Get_NotAtTheCounter_IsRefused()
    {
        Guid player = await CreatePlayerAsync();
        _presence.Stand(player, GameMaps.Town, 10 * 16, 10 * 16);

        BackpackView view = await _backpack.GetAsync(_connection);

        view.Should().Be(new BackpackView(BackpackStatus.NotAtCounter, 0, 0, 0));
    }

    [Fact]
    public async Task Get_AtTheTailor_ReadsTheSizeAndTheNextUpgrade()
    {
        Guid player = await CreatePlayerAsync();
        StandAtCounter(player, ShopCatalog.ClothingShop);

        BackpackView view = await _backpack.GetAsync(_connection);

        view.Should().Be(new BackpackView(BackpackStatus.Ok, 36, 45, 600));
    }

    [Fact]
    public async Task Upgrade_NotEnoughCoins_IsRefused_AndTheSizeStays()
    {
        Guid player = await CreatePlayerAsync();
        StandAtCounter(player, ShopCatalog.ClothingShop);

        BackpackView view = await _backpack.UpgradeAsync(_connection); // 500 coins, the next size costs 600

        view.Should().Be(new BackpackView(BackpackStatus.NotEnoughCoins, 36, 45, 600));
        (await _transfers.GetBalanceAsync(player)).Should().Be(500);
        _inventory.Forget(player);
        (await _inventory.GetAsync(player)).Capacity.Should().Be(InventoryLimits.DefaultCapacity);
    }

    [Fact]
    public async Task Upgrade_AtTheLargestSize_IsRefused_AndChargesNothing()
    {
        Guid player = await CreatePlayerAsync();
        StandAtCounter(player, ShopCatalog.ClothingShop);
        await GiveCoinsAsync(player, 9_500); // 10 000 coins: enough for both sizes and more

        (await _backpack.UpgradeAsync(_connection)).Status.Should().Be(BackpackStatus.Ok);
        (await _backpack.UpgradeAsync(_connection)).Status.Should().Be(BackpackStatus.Ok);
        BackpackView view = await _backpack.UpgradeAsync(_connection);

        view.Should().Be(new BackpackView(BackpackStatus.MaxSize, 54, 0, 0));
        (await _transfers.GetBalanceAsync(player)).Should().Be(10_000 - 600 - 1_000);
    }

    [Fact]
    public async Task Upgrade_TwoAtOnceWithCoinsForOneSize_OneSucceeds_AndTheOtherChangesNothing()
    {
        Guid player = await CreatePlayerAsync();
        StandAtCounter(player, ShopCatalog.ClothingShop);
        await GiveCoinsAsync(player, 100); // exactly one 600-coin size

        BackpackView[] views = await Task.WhenAll(_backpack.UpgradeAsync(_connection), _backpack.UpgradeAsync(_connection));

        views.Count(v => v.Status == BackpackStatus.Ok).Should().Be(1);
        views.Where(v => v.Status != BackpackStatus.Ok).Select(v => v.Status).Should().OnlyContain(s => s == BackpackStatus.Conflict || s == BackpackStatus.NotEnoughCoins);
        (await _transfers.GetBalanceAsync(player)).Should().Be(0);
        _inventory.Forget(player);
        (await _inventory.GetAsync(player)).Capacity.Should().Be(45);
    }

    [Fact]
    public async Task Execute_TwoGrowthsToTheSameSizeAtOnce_OneIsConflictAndOneChargeIsTaken()
    {
        Guid player = await CreatePlayerAsync();
        await GiveCoinsAsync(player, 700); // 1 200 coins: two charges would be possible for money, not for the size

        TransferStatus[] results = await Task.WhenAll(
            _transfers.ExecuteWithOperationsAsync(player, [InventoryOperation.Grow(45)], [new TransferLeg(TransferParty.Player(player), 0, 0, -600)], "test.grow"),
            _transfers.ExecuteWithOperationsAsync(player, [InventoryOperation.Grow(45)], [new TransferLeg(TransferParty.Player(player), 0, 0, -600)], "test.grow"));

        results.Count(r => r == TransferStatus.Ok).Should().Be(1);
        results.Count(r => r == TransferStatus.Conflict).Should().Be(1);
        (await _transfers.GetBalanceAsync(player)).Should().Be(600);
        (await _inventory.GetAsync(player)).Capacity.Should().Be(45);
    }

    [Fact]
    public async Task Grow_ChestIsNeverGrown_AndChangesNothing()
    {
        Guid chest = ContainerIds.ForChest(4242);

        InventoryChange change = await _inventory.ApplyAsync(chest, [InventoryOperation.Grow(45)], "test.grow");

        change.Status.Should().Be(InventoryStatus.Conflict);
        _inventory.Forget(chest);
        (await _inventory.GetAsync(chest)).Capacity.Should().Be(ChestRules.Capacity);
    }

    [Theory]
    [InlineData(36)]
    [InlineData(30)]
    [InlineData(60)]
    public async Task Grow_ToASizeThatIsNotLargerOrAboveTheMaximum_IsRefused_AndTheAddsBeforeItAreNotSaved(int target)
    {
        Guid player = await CreatePlayerAsync();
        int seedsBefore = await CountAsync(player, ItemCatalog.ParsnipSeeds);

        InventoryChange change = await _inventory.ApplyAsync(player, [InventoryOperation.Add(ItemCatalog.ParsnipSeeds, 2), InventoryOperation.Grow(target)], "test.grow");

        change.Status.Should().Be(InventoryStatus.Conflict);
        _inventory.Forget(player);
        (await _inventory.GetAsync(player)).Capacity.Should().Be(InventoryLimits.DefaultCapacity);
        (await CountAsync(player, ItemCatalog.ParsnipSeeds)).Should().Be(seedsBefore);
    }

    [Fact]
    public async Task Grow_AfterAnAddInTheSameChange_SavesTheAddedItemsToo()
    {
        Guid player = await CreatePlayerAsync();
        int seedsBefore = await CountAsync(player, ItemCatalog.ParsnipSeeds);

        InventoryChange change = await _inventory.ApplyAsync(player, [InventoryOperation.Add(ItemCatalog.ParsnipSeeds, 3), InventoryOperation.Grow(45)], "test.add-grow");

        change.Status.Should().Be(InventoryStatus.Ok);
        _inventory.Forget(player);
        (await _inventory.GetAsync(player)).Capacity.Should().Be(45);
        (await CountAsync(player, ItemCatalog.ParsnipSeeds)).Should().Be(seedsBefore + 3, "the rows were written with the growth, not only the copy");
    }

    [Fact]
    public async Task Grow_AndAPlacementInTheNewSlotInOneChange_FromTheCopyInMemory_SavesAndStoresTheBag()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _inventory.GetAsync(player); // the copy is in memory, so the change works on it and not on rows

        InventoryChange change = await _inventory.ApplyAsync(player, [InventoryOperation.Grow(45), InventoryOperation.AddToSlot(40, ItemCatalog.ShirtRed, 1)], "test.grow-place");

        change.Status.Should().Be(InventoryStatus.Ok);
        (await _inventory.GetAsync(player)).Capacity.Should().Be(45, "the copy in memory holds the new size");
        _inventory.Forget(player);
        (await _inventory.GetAsync(player)).Capacity.Should().Be(45, "the rows hold the new size");
        (await _inventory.PeekSlotAsync(player, 40)).Should().Be(new InventorySlotView(40, ItemCatalog.ShirtRed, 1));
    }

    private void StandAtCounter(Guid player, int shopId)
    {
        ShopPoint point = TownSpec().Shops.Single(p => p.ShopId == shopId);
        _presence.Stand(player, GameMaps.Town, (point.TileX * 16) + 8, (point.TileY * 16) + 8);
    }

    private static MapSpec TownSpec()
    {
        _ = GameMaps.TryGet(GameMaps.Town, out MapSpec? town);
        return town!;
    }

    private async Task GiveCoinsAsync(Guid player, long coins)
    {
        TransferStatus status = await _transfers.ExecuteAsync([new TransferLeg(TransferParty.Player(player), 0, 0, coins)], "test.setup");
        status.Should().Be(TransferStatus.Ok);
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
            _ = await unit.SaveChangesAsync(System.Threading.CancellationToken.None);
            return player.Id;
        }
    }

    // A connection nobody talks to: presence only stores it.
    private class ConnectionStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod is { ReturnType: { IsValueType: true } type } && type != typeof(void) ? Activator.CreateInstance(type) : null;
    }

    private sealed class TownCatalog : IMapCatalog
    {
        public bool TryGet(string mapId, [NotNullWhen(true)] out MapDefinition? map)
        {
            // Only the tile size matters to the counter; every tile of the blank grid is walkable.
            map = new MapDefinition(mapId, new WalkGrid(130, 110, 16, new bool[130 * 110]), 0, 0);
            return true;
        }
    }

    // Reports one player at a chosen place; everything else is empty.
    private sealed class StandingPresence : IMapPresence
    {
        private PlayerPresence? _player;

        public event Func<PlayerPresence, ValueTask>? PlayerLeft
        {
            add { }
            remove { }
        }

        public int PlayerCount => _player is null ? 0 : 1;

        [SuppressMessage("Reliability", "CA2000", Justification = "A stub connection that holds nothing to dispose.")]
        public void Stand(Guid account, string mapId, double x, double y)
            => _player = new PlayerPresence(1, account, new MapInstance(mapId, Guid.Empty), DispatchProxy.Create<IConnection, ConnectionStub>(), new MotionState(x, y, 0));

        public void Remove() => _player = null;

        public ValueTask<PlayerPresence> JoinAsync(IConnection connection, Guid accountId, MapInstance instance, MotionState motion, uint? playerId = null, System.Threading.CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask LeaveAsync(IConnection connection, System.Threading.CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public bool TryGet(IConnection connection, [NotNullWhen(true)] out PlayerPresence? player)
        {
            player = _player;
            return player is not null;
        }

        public void CopyActiveMapIds(List<string> into)
        {
        }

        public void CopyPlayers(string mapId, List<PlayerPresence> into)
        {
        }

        public ValueTask SendAsync(IConnection connection, IPacket packet, System.Threading.CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask MulticastAsync(string mapId, IPacket packet, IConnection? except = null, System.Threading.CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}

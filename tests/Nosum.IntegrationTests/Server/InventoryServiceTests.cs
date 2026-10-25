// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Domain.Accounts;
using Nosum.Domain.Inventory;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>The inventory service against a real PostgreSQL database: stacking, atomic changes, audit and pushes.</summary>
public sealed class InventoryServiceTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly RecordingNotifier _notifier = new();
    private readonly EfUnitOfWorkFactory _units;
    private readonly InventoryService _inventory;

    public InventoryServiceTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _inventory = new InventoryService(_units, _clock, _notifier);
    }

    [Fact]
    public async Task GetAsync_NewPlayer_GetsTheStarterKitOnce()
    {
        Guid player = await CreatePlayerAsync();

        InventoryView first = await _inventory.GetAsync(player);
        InventoryView second = await _inventory.GetAsync(player);

        first.Capacity.Should().Be(InventoryLimits.DefaultCapacity);
        first.Slots.Select(s => (s.ItemId, s.Quantity)).Should().Equal(StarterKit.Items.Select(i => (i.ItemId, i.Quantity)));
        second.Slots.Should().Equal(first.Slots);
        (await LogOfAsync(player)).Should().HaveCount(StarterKit.Items.Length).And.OnlyContain(e => e.Reason == "starter" && e.Delta > 0);
    }

    [Fact]
    public async Task AddAsync_OntoExistingStack_TopsItUpAndSurvivesANewServiceInstance()
    {
        Guid player = await CreatePlayerAsync();

        InventoryChange change = await _inventory.AddAsync(player, ItemCatalog.ParsnipSeeds, 5, "test.add");

        change.Status.Should().Be(InventoryStatus.Ok);
        change.Changed.Should().ContainSingle().Which.Should().Be(new InventorySlotView(2, ItemCatalog.ParsnipSeeds, 13));
        InventoryService other = new(_units, _clock);
        (await other.PeekSlotAsync(player, 2)).Quantity.Should().Be(13);
    }

    [Fact]
    public async Task AddAsync_BeyondTheStackLimit_SpillsIntoANewSlot()
    {
        Guid player = await CreatePlayerAsync();

        InventoryChange change = await _inventory.AddAsync(player, ItemCatalog.Parsnip, 150, "test.add");

        change.Status.Should().Be(InventoryStatus.Ok);
        change.Changed.Select(c => c.Quantity).Should().Equal(99, 51);
        (await _inventory.GetAsync(player)).Slots.Where(s => s.ItemId == ItemCatalog.Parsnip).Sum(s => s.Quantity).Should().Be(150);
    }

    [Fact]
    public async Task ApplyAsync_WhenALaterOperationFails_ChangesNothingAndLogsNothing()
    {
        Guid player = await CreatePlayerAsync();
        InventoryView before = await _inventory.GetAsync(player);
        int logBefore = (await LogOfAsync(player)).Count;

        InventoryChange change = await _inventory.ApplyAsync(
            player,
            [InventoryOperation.Add(ItemCatalog.Parsnip, 5), InventoryOperation.Remove(ItemCatalog.Cauliflower, 100)],
            "test.atomic");

        change.Status.Should().Be(InventoryStatus.NotEnough);
        change.Changed.Should().BeEmpty();
        (await _inventory.GetAsync(player)).Slots.Should().Equal(before.Slots);
        (await LogOfAsync(player)).Should().HaveCount(logBefore);
    }

    [Fact]
    public async Task ApplyAsync_WhenTheEnlistedWorkRefuses_RollsTheItemsBack()
    {
        Guid player = await CreatePlayerAsync();
        InventoryView before = await _inventory.GetAsync(player);

        InventoryChange change = await _inventory.ApplyAsync(
            player,
            [InventoryOperation.RemoveFromSlot(2, ItemCatalog.ParsnipSeeds, 1)],
            "test.enlist",
            (_, _) => Task.FromResult(false));

        change.Status.Should().Be(InventoryStatus.Conflict);
        (await _inventory.GetAsync(player)).Slots.Should().Equal(before.Slots);
    }

    [Fact]
    public async Task ApplyAsync_WithEnlistedWork_SavesBothInOneTransaction()
    {
        Guid player = await CreatePlayerAsync();
        string marker = "test.marker";

        InventoryChange change = await _inventory.ApplyAsync(
            player,
            [InventoryOperation.RemoveFromSlot(2, ItemCatalog.ParsnipSeeds, 1)],
            "test.enlist",
            (unit, _) =>
            {
                unit.Repository<InventoryLogEntry>().Add(new InventoryLogEntry { PlayerId = player, ItemId = 0, Delta = 0, Reason = marker, OccurredAt = s_start });
                return Task.FromResult(true);
            });

        change.Status.Should().Be(InventoryStatus.Ok);
        (await LogOfAsync(player)).Should().Contain(e => e.Reason == marker).And.Contain(e => e.Reason == "test.enlist" && e.Delta == -1);
    }

    [Fact]
    public async Task RemoveAsync_RecordsANegativeAuditEntryWithTheReason()
    {
        Guid player = await CreatePlayerAsync();

        _ = await _inventory.RemoveAsync(player, ItemCatalog.RadishSeeds, 2, "test.remove");

        (await LogOfAsync(player)).Should().Contain(e => e.Reason == "test.remove" && e.ItemId == ItemCatalog.RadishSeeds && e.Delta == -2 && e.OccurredAt == s_start);
    }

    [Fact]
    public async Task MoveAsync_ToAnEmptySlot_MovesAndPersists()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _inventory.GetAsync(player);

        InventoryChange change = await _inventory.MoveAsync(player, 0, 20, 0);

        change.Status.Should().Be(InventoryStatus.Ok);
        change.Changed.Should().BeEquivalentTo(new[] { new InventorySlotView(0, 0, 0), new InventorySlotView(20, ItemCatalog.Hoe, 1) });
        InventoryView view = await new InventoryService(_units, _clock).GetAsync(player);
        view.Slots.Should().Contain(new InventorySlotView(20, ItemCatalog.Hoe, 1)).And.NotContain(s => s.Slot == 0);
    }

    [Fact]
    public async Task MoveAsync_OntoAnotherItem_SwapsTheStacks()
    {
        Guid player = await CreatePlayerAsync();

        InventoryChange change = await _inventory.MoveAsync(player, 0, 1, 0);

        change.Status.Should().Be(InventoryStatus.Ok);
        (await _inventory.PeekSlotAsync(player, 0)).ItemId.Should().Be(ItemCatalog.WateringCan);
        (await _inventory.PeekSlotAsync(player, 1)).ItemId.Should().Be(ItemCatalog.Hoe);
    }

    [Fact]
    public async Task MoveAsync_FromAnEmptySlot_IsRefusedWithoutAChange()
    {
        Guid player = await CreatePlayerAsync();

        InventoryChange change = await _inventory.MoveAsync(player, 30, 31, 0);

        change.Status.Should().Be(InventoryStatus.EmptySlot);
        change.Changed.Should().BeEmpty();
        _notifier.Updates.Should().BeEmpty();
    }

    [Fact]
    public async Task MoveAsync_OutsideTheInventory_IsRefused()
    {
        Guid player = await CreatePlayerAsync();

        (await _inventory.MoveAsync(player, 0, 99, 0)).Status.Should().Be(InventoryStatus.InvalidSlot);
    }

    [Fact]
    public async Task AddAsync_ToAFullInventory_IsRefusedWithNoSpace()
    {
        Guid player = await CreatePlayerAsync();
        for (int i = 0; i < InventoryLimits.DefaultCapacity; i++)
        {
            _ = await _inventory.AddAsync(player, ItemCatalog.Radish, 99, "test.fill");
            if ((await _inventory.GetAsync(player)).Slots.Count == InventoryLimits.DefaultCapacity)
            {
                break;
            }
        }

        InventoryChange change = await _inventory.AddAsync(player, ItemCatalog.Cauliflower, 1, "test.full");

        change.Status.Should().Be(InventoryStatus.NoSpace);
    }

    [Fact]
    public async Task AddAsync_ManyAtOnce_NeverLosesAnUpdate()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _inventory.GetAsync(player);

        InventoryChange[] changes = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => _inventory.AddAsync(player, ItemCatalog.Parsnip, 1, "test.parallel")));

        changes.Should().OnlyContain(c => c.Status == InventoryStatus.Ok);
        (await _inventory.GetAsync(player)).Slots.Where(s => s.ItemId == ItemCatalog.Parsnip).Sum(s => s.Quantity).Should().Be(25);
    }

    [Fact]
    public async Task SelectSlotAsync_Hotbar_IsRememberedAndOutsideItIsRefused()
    {
        Guid player = await CreatePlayerAsync();

        (InventoryStatus ok, int selected) = await _inventory.SelectSlotAsync(player, 4);
        (InventoryStatus refused, int unchanged) = await _inventory.SelectSlotAsync(player, 9);

        ok.Should().Be(InventoryStatus.Ok);
        selected.Should().Be(4);
        refused.Should().Be(InventoryStatus.InvalidSlot);
        unchanged.Should().Be(4);
        (await new InventoryService(_units, _clock).GetAsync(player)).SelectedSlot.Should().Be(4);
    }

    [Fact]
    public async Task AddAsync_PushesTheChangedSlotsToThePlayer()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _inventory.GetAsync(player);
        _notifier.Updates.Clear();

        _ = await _inventory.AddAsync(player, ItemCatalog.Parsnip, 3, "test.push");

        (Guid to, List<(int Slot, int ItemId, int Quantity)> rows) = _notifier.Updates.Should().ContainSingle().Subject;
        to.Should().Be(player);
        // Produce goes to the first empty slot after the starter kit.
        rows.Should().Equal((StarterKit.Items.Length, ItemCatalog.Parsnip, 3));
    }

    [Fact]
    public async Task ApplyAsync_UnknownItem_IsRefused()
    {
        Guid player = await CreatePlayerAsync();

        (await _inventory.AddAsync(player, 4242, 1, "test.unknown")).Status.Should().Be(InventoryStatus.UnknownItem);
    }

    private async Task<Guid> CreatePlayerAsync()
    {
        IUnitOfWork unit = _units.Create();
        await using (unit)
        {
            Player player = new() { IsGuest = true, CreatedAt = s_start, LastActiveAt = s_start };
            unit.Repository<Player>().Add(player);
            _ = await unit.SaveChangesAsync(CancellationToken.None);
            return player.Id;
        }
    }

    private async Task<List<InventoryLogEntry>> LogOfAsync(Guid player)
    {
        await using NosumDbContext context = new(_database.Options);
        return await context.InventoryLog.Where(e => e.PlayerId == player).ToListAsync();
    }

    private sealed class RecordingNotifier : IPlayerNotifier
    {
        public List<(Guid Player, List<(int Slot, int ItemId, int Quantity)> Rows)> Updates { get; } = [];

        public ValueTask<bool> TrySendAsync(Guid playerId, IPacket packet, CancellationToken cancellationToken = default)
        {
            if (packet is InventoryUpdate update)
            {
                this.Updates.Add((playerId, update.Slots.Select(s => ((int)s.Slot, s.ItemId, s.Quantity)).ToList()));
            }

            return ValueTask.FromResult(true);
        }
    }
}

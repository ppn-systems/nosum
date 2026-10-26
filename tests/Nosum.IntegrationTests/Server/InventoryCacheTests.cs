// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.GroundItems;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Nodes;
using Nosum.Contracts.Inventory;
using Nosum.Domain.Accounts;
using Nosum.Domain.Inventory;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>The in-memory inventory of docs/adr/ADR-035 D3 stays equal to the rows: after parallel transfers, after a failed save, after a container is dropped, and by who may write the tables.</summary>
public sealed partial class InventoryCacheTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly EfUnitOfWorkFactory _units;
    private readonly InventoryService _inventory;

    public InventoryCacheTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _inventory = new InventoryService(_units, _clock);
    }

    [Fact]
    public async Task ApplyManyAsync_ParallelTransfersBetweenPlayers_KeepEveryCachedGridEqualToTheRows()
    {
        Guid a = await CreatePlayerAsync();
        Guid b = await CreatePlayerAsync();
        Guid c = await CreatePlayerAsync();
        _ = await _inventory.AddAsync(a, ItemCatalog.Parsnip, 60, "test.seed");
        _ = await _inventory.AddAsync(b, ItemCatalog.Parsnip, 60, "test.seed");
        _ = await _inventory.AddAsync(c, ItemCatalog.Parsnip, 60, "test.seed");

        Guid[] players = [a, b, c];
        List<Task<InventoryStatus>> moves = [];
        for (int i = 0; i < 60; i++)
        {
            Guid from = players[i % 3];
            Guid to = players[(i + 1) % 3];
            moves.Add(_inventory.ApplyManyAsync(
                [new PlayerInventoryChange(from, [InventoryOperation.Remove(ItemCatalog.Parsnip, 2)]), new PlayerInventoryChange(to, [InventoryOperation.Add(ItemCatalog.Parsnip, 2)])],
                "test.transfer"));
            moves.Add(_inventory.AddAsync(from, ItemCatalog.Radish, 1, "test.single").ContinueWith(t => t.Result.Status, TaskScheduler.Default));
        }

        InventoryStatus[] results = await Task.WhenAll(moves);

        results.Should().OnlyContain(s => s == InventoryStatus.Ok);
        foreach (Guid player in players)
        {
            await AssertCacheEqualsRowsAsync(player);
        }

        int total = 0;
        foreach (Guid player in players)
        {
            total += (await _inventory.GetAsync(player)).Slots.Where(s => s.ItemId == ItemCatalog.Parsnip).Sum(s => s.Quantity);
        }

        total.Should().Be(180, "no transfer created or lost an item");
    }

    [Fact]
    public async Task ApplyAsync_RefusedHalfWay_LeavesTheCachedGridAsItWas()
    {
        Guid player = await CreatePlayerAsync();
        InventoryView before = await _inventory.GetAsync(player);

        InventoryChange change = await _inventory.ApplyAsync(player, [InventoryOperation.Add(ItemCatalog.Parsnip, 5), InventoryOperation.Remove(ItemCatalog.Cauliflower, 100)], "test.refused");

        change.Status.Should().Be(InventoryStatus.NotEnough);
        (await _inventory.GetAsync(player)).Slots.Should().Equal(before.Slots);
        await AssertCacheEqualsRowsAsync(player);
    }

    [Fact]
    public async Task ApplyAsync_SaveFailsWithADbUpdateException_DropsTheEntryAndTheNextReadReloads()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _inventory.AddAsync(player, ItemCatalog.Parsnip, 5, "test.seed"); // cached now
        int parsnipSlot = (await _inventory.GetAsync(player)).Slots.Single(s => s.ItemId == ItemCatalog.Parsnip).Slot;

        // The rows change behind the service's back: the cache is now stale on purpose.
        await using (NosumDbContext db = new(_database.Options))
        {
            _ = await db.InventorySlots.Where(s => s.PlayerId == player && s.Slot == parsnipSlot).ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, 9));
        }

        (await _inventory.PeekSlotAsync(player, parsnipSlot)).Quantity.Should().Be(5, "a read is answered from memory");

        // A change whose save fails: the enlisted work inserts a log entry twice with the same key.
        Guid duplicate = Guid.NewGuid();
        Func<Task> failing = () => _inventory.ApplyAsync(
            player,
            [InventoryOperation.Add(ItemCatalog.Radish, 1)],
            "test.fail",
            (unit, _) =>
            {
                unit.Repository<InventoryLogEntry>().Add(new InventoryLogEntry { Id = duplicate, PlayerId = player, ItemId = 0, Delta = 0, Reason = "dup", OccurredAt = s_start });
                unit.Repository<InventoryLogEntry>().Add(new InventoryLogEntry { Id = duplicate, PlayerId = player, ItemId = 0, Delta = 0, Reason = "dup", OccurredAt = s_start });
                return Task.FromResult(true);
            });
        _ = await failing.Should().ThrowAsync<Exception>();

        (await _inventory.PeekSlotAsync(player, parsnipSlot)).Quantity.Should().Be(9, "the entry was dropped, so the read went to the rows");
        await AssertCacheEqualsRowsAsync(player);
    }

    [Fact]
    public async Task ApplyAsync_SaveFailsWithAUniqueViolation_RefusesAsConflictAndDropsTheEntry()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _inventory.AddAsync(player, ItemCatalog.Parsnip, 5, "test.seed");
        int parsnipSlot = (await _inventory.GetAsync(player)).Slots.Single(s => s.ItemId == ItemCatalog.Parsnip).Slot;
        Guid existing;
        await using (NosumDbContext db = new(_database.Options))
        {
            existing = (await db.InventoryLog.FirstAsync(l => l.PlayerId == player)).Id;
            _ = await db.InventorySlots.Where(s => s.PlayerId == player && s.Slot == parsnipSlot).ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, 7));
        }

        InventoryChange change = await _inventory.ApplyAsync(
            player,
            [InventoryOperation.Add(ItemCatalog.Radish, 1)],
            "test.conflict",
            (unit, _) =>
            {
                unit.Repository<InventoryLogEntry>().Add(new InventoryLogEntry { Id = existing, PlayerId = player, ItemId = 0, Delta = 0, Reason = "dup", OccurredAt = s_start });
                return Task.FromResult(true);
            });

        change.Status.Should().Be(InventoryStatus.Conflict);
        (await _inventory.PeekSlotAsync(player, parsnipSlot)).Quantity.Should().Be(7);
        (await _inventory.GetAsync(player)).Slots.Should().NotContain(s => s.ItemId == ItemCatalog.Radish, "nothing of the failed change was kept");
    }

    [Fact]
    public async Task ApplyAsync_TheRowIsGoneBehindTheCache_ConflictsAndReloads()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _inventory.AddAsync(player, ItemCatalog.Parsnip, 5, "test.seed");
        int parsnipSlot = (await _inventory.GetAsync(player)).Slots.Single(s => s.ItemId == ItemCatalog.Parsnip).Slot;
        await using (NosumDbContext db = new(_database.Options))
        {
            _ = await db.InventorySlots.Where(s => s.PlayerId == player && s.Slot == parsnipSlot).ExecuteDeleteAsync();
        }

        InventoryChange change = await _inventory.AddAsync(player, ItemCatalog.Parsnip, 1, "test.stale");

        change.Status.Should().Be(InventoryStatus.Conflict, "updating a row that no longer exists is a lost race, not a silent success");
        (await _inventory.GetAsync(player)).Slots.Should().NotContain(s => s.ItemId == ItemCatalog.Parsnip);
        await AssertCacheEqualsRowsAsync(player);
    }

    [Fact]
    public async Task SelectSlotAsync_IsWrittenAndTheNextReadSeesIt()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _inventory.GetAsync(player);

        _ = await _inventory.SelectSlotAsync(player, 5);

        (await _inventory.GetAsync(player)).SelectedSlot.Should().Be(5);
        await AssertCacheEqualsRowsAsync(player);
    }

    [Fact]
    public async Task DropContainerAsync_AnEmptyChest_DeletesTheHeaderAndTheEntry()
    {
        Guid chest = ContainerIds.ForChest(await NextChestIdAsync());
        _ = await _inventory.GetAsync(chest);

        bool dropped = await DropAsync(chest, onlyIfEmpty: true);

        dropped.Should().BeTrue();
        await using (NosumDbContext db = new(_database.Options))
        {
            (await db.PlayerInventories.AnyAsync(i => i.PlayerId == chest)).Should().BeFalse();
        }

        _ = await _inventory.AddAsync(chest, ItemCatalog.Wood, 3, "test.again"); // a new header is created, which fails if a stale entry thought it existed
        await AssertCacheEqualsRowsAsync(chest);
    }

    [Fact]
    public async Task DropContainerAsync_AChestHoldingItems_IsLeftAloneUnlessForced()
    {
        Guid chest = ContainerIds.ForChest(await NextChestIdAsync());
        _ = await _inventory.AddAsync(chest, ItemCatalog.Wood, 3, "test.fill");

        (await DropAsync(chest, onlyIfEmpty: true)).Should().BeFalse();
        (await _inventory.GetAsync(chest)).Slots.Should().ContainSingle();
        await using (NosumDbContext db = new(_database.Options))
        {
            (await db.InventorySlots.CountAsync(s => s.PlayerId == chest)).Should().Be(1);
        }

        (await DropAsync(chest, onlyIfEmpty: false)).Should().BeTrue();
        await using (NosumDbContext db = new(_database.Options))
        {
            (await db.PlayerInventories.AnyAsync(i => i.PlayerId == chest)).Should().BeFalse();
            (await db.InventorySlots.AnyAsync(s => s.PlayerId == chest)).Should().BeFalse("the rows go with the header");
        }

        (await _inventory.GetAsync(chest)).Slots.Should().BeEmpty("the entry went with the rows");
    }

    [Fact]
    public async Task AnyContainerHoldsItemsAsync_ReadsMemoryAndRows()
    {
        Guid full = ContainerIds.ForChest(await NextChestIdAsync());
        Guid empty = ContainerIds.ForChest(await NextChestIdAsync());
        _ = await _inventory.AddAsync(full, ItemCatalog.Wood, 3, "test.fill");
        _ = await _inventory.GetAsync(empty);
        InventoryService cold = new(_units, _clock);

        IUnitOfWork unit = _units.Create();
        await using (unit)
        {
            (await _inventory.AnyContainerHoldsItemsAsync(unit, [empty], CancellationToken.None)).Should().BeFalse();
            (await _inventory.AnyContainerHoldsItemsAsync(unit, [empty, full], CancellationToken.None)).Should().BeTrue();
            (await cold.AnyContainerHoldsItemsAsync(unit, [empty, full], CancellationToken.None)).Should().BeTrue("a service with nothing in memory asks the rows");
            (await cold.AnyContainerHoldsItemsAsync(unit, [empty], CancellationToken.None)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task InventoryTables_AreWrittenOnlyByInventoryService()
    {
        string infrastructure = Path.Combine(RepoRoot(), "server", "Nosum.Infrastructure");
        string[] allowed = ["Inventory/InventoryService.cs", "Persistence/NosumDbContext.cs"];
        List<string> offenders = [];
        foreach (string file in Directory.EnumerateFiles(infrastructure, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(infrastructure, file).Replace('\\', '/');
            if (relative.StartsWith("Persistence/Migrations/", StringComparison.Ordinal) || relative.StartsWith("Persistence/Configurations/", StringComparison.Ordinal) || allowed.Contains(relative) || relative.Contains("/obj/", StringComparison.Ordinal))
            {
                continue;
            }

            if (InventoryTables().IsMatch(await File.ReadAllTextAsync(file)))
            {
                offenders.Add(relative);
            }
        }

        offenders.Should().BeEmpty("the inventory tables are changed in one place so the in-memory copy can follow every change (docs/adr/ADR-035 D3 rule 5); go through IInventoryService");
    }

    [Fact]
    public async Task TransferGroundItemAndYieldImplementations_MoveItemsOnlyThroughTheInventoryService()
    {
        Type[] contracts = [typeof(ITransferService), typeof(IGroundItems), typeof(INodeYieldSink)];
        string infrastructure = Path.Combine(RepoRoot(), "server", "Nosum.Infrastructure");
        string[] sources = Directory.EnumerateFiles(infrastructure, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("/obj/", StringComparison.Ordinal)).ToArray();
        Type[] implementations = [.. typeof(InventoryService).Assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && contracts.Any(c => c.IsAssignableFrom(t)))];
        implementations.Should().NotBeEmpty();

        foreach (Type type in implementations)
        {
            string file = sources.Single(f => Regex.IsMatch(File.ReadAllText(f), @"\bclass " + Regex.Escape(type.Name) + @"\b"));
            string text = await File.ReadAllTextAsync(file);
            string[] forbidden = ["InventoryLogEntry", "Repository<InventorySlot>", "Repository<PlayerInventory>"];
            forbidden.Where(text.Contains).Should().BeEmpty(type.Name + " must change items through IInventoryService");
        }
    }

    [GeneratedRegex(@"\b(InventorySlot|PlayerInventory|InventorySlots|PlayerInventories|InventoryLogEntry|InventoryLog)\b")]
    private static partial Regex InventoryTables();

    // What the service answers from memory against what a service with nothing in memory reads from the rows.
    private async Task AssertCacheEqualsRowsAsync(Guid owner)
    {
        InventoryView cached = await _inventory.GetAsync(owner);
        InventoryView rows = await new InventoryService(_units, _clock).GetAsync(owner);
        cached.Capacity.Should().Be(rows.Capacity);
        cached.SelectedSlot.Should().Be(rows.SelectedSlot);
        cached.Slots.Should().Equal(rows.Slots);
    }

    private async Task<bool> DropAsync(Guid container, bool onlyIfEmpty)
    {
        IUnitOfWork unit = _units.Create();
        await using (unit)
        {
            bool dropped = await _inventory.DropContainerAsync(unit, container, onlyIfEmpty, CancellationToken.None);
            _ = await unit.SaveChangesAsync(CancellationToken.None);
            return dropped;
        }
    }

    private static int s_nextChest = 1_000_000;

    private static Task<int> NextChestIdAsync() => Task.FromResult(Interlocked.Increment(ref s_nextChest));

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

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}

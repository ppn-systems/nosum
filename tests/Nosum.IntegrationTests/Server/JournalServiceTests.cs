// Copyright (c) 2026 PPN Systems. All rights reserved.

#pragma warning disable CA2007

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Journal;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Journal;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The collection journal against a real PostgreSQL database: every grant counts into the journal in the same transaction, a refused grant or a move
/// does not, and a player reads only their own journal.
/// </summary>
public sealed class JournalServiceTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly EfUnitOfWorkFactory _units;
    private readonly InventoryService _inventory;
    private readonly JournalService _journal;

    public JournalServiceTests(PostgresFixture database)
    {
        _units = new EfUnitOfWorkFactory(database.Options);
        _inventory = new InventoryService(_units, _clock);
        _journal = new JournalService(_units);
    }

    [Fact]
    public async Task Grant_ThenRead_ShowsTheItemWithItsCountAndFirstTime()
    {
        Guid player = await CreatePlayerAsync();

        InventoryChange change = await _inventory.AddAsync(player, ItemCatalog.Parsnip, 3, "test.grant");

        change.IsOk.Should().BeTrue();
        JournalLine line = await LineOfAsync(player, ItemCatalog.Parsnip);
        line.Count.Should().Be(3);
        line.FirstObtainedAt.Should().Be(s_start);
    }

    [Fact]
    public async Task LaterGrants_CountUpAndKeepTheFirstTime()
    {
        Guid player = await CreatePlayerAsync();
        await _inventory.AddAsync(player, ItemCatalog.Parsnip, 3, "test.grant");
        _clock.Advance(TimeSpan.FromHours(5));

        await _inventory.AddAsync(player, ItemCatalog.Parsnip, 2, "test.grant");

        JournalLine line = await LineOfAsync(player, ItemCatalog.Parsnip);
        line.Count.Should().Be(5);
        line.FirstObtainedAt.Should().Be(s_start);
    }

    [Fact]
    public async Task Grant_OfSeveralItems_ReadsThemLowestItemIdFirst()
    {
        Guid player = await CreatePlayerAsync();
        await _inventory.AddAsync(player, ItemCatalog.Wood, 1, "test.grant");
        await _inventory.AddAsync(player, ItemCatalog.Parsnip, 1, "test.grant");

        int[] ids = [.. (await _journal.GetAsync(player)).Select(l => l.ItemId)];

        ids.Should().BeInAscendingOrder().And.Contain(new[] { ItemCatalog.Parsnip, ItemCatalog.Wood });
    }

    [Fact]
    public async Task Refused_Grant_RecordsNothing()
    {
        Guid player = await CreatePlayerAsync();

        InventoryChange change = await _inventory.AddAsync(player, 4242, 1, "test.grant");

        change.IsOk.Should().BeFalse();
        (await _journal.GetAsync(player)).Should().BeEmpty("a refused change saves nothing, not even the starter kit");
    }

    [Fact]
    public async Task Removal_DoesNotLowerTheCount()
    {
        Guid player = await CreatePlayerAsync();
        await _inventory.AddAsync(player, ItemCatalog.Parsnip, 3, "test.grant");

        await _inventory.RemoveAsync(player, ItemCatalog.Parsnip, 2, "test.spend");

        (await LineOfAsync(player, ItemCatalog.Parsnip)).Count.Should().Be(3);
    }

    [Fact]
    public async Task MoveAndSort_ObtainNothing()
    {
        Guid player = await CreatePlayerAsync();
        await _inventory.AddAsync(player, ItemCatalog.Parsnip, 3, "test.grant");

        await _inventory.SortAsync(player, 0);

        (await LineOfAsync(player, ItemCatalog.Parsnip)).Count.Should().Be(3);
    }

    [Fact]
    public async Task NewPlayer_JournalStartsWithTheStarterKit()
    {
        Guid player = await CreatePlayerAsync();

        _ = await _inventory.GetAsync(player);

        int[] ids = [.. (await _journal.GetAsync(player)).Select(l => l.ItemId)];
        ids.Should().BeEquivalentTo(StarterKit.Items.Select(i => i.ItemId).Distinct());
    }

    [Fact]
    public async Task ConcurrentGrants_AllCount_IntoOneEntry()
    {
        Guid player = await CreatePlayerAsync();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => _inventory.AddAsync(player, ItemCatalog.Parsnip, 1, "test.grant")));

        (await LineOfAsync(player, ItemCatalog.Parsnip)).Count.Should().Be(20);
    }

    [Fact]
    public async Task Journal_IsPerPlayer()
    {
        Guid alice = await CreatePlayerAsync();
        Guid bob = await CreatePlayerAsync();
        await _inventory.AddAsync(alice, ItemCatalog.Parsnip, 2, "test.grant");

        (await _journal.GetAsync(bob)).Should().NotContain(l => l.ItemId == ItemCatalog.Parsnip);
        (await LineOfAsync(alice, ItemCatalog.Parsnip)).Count.Should().Be(2);
    }

    // The journal of a player holds the starter kit too, so a test reads the one item it grants.
    private async Task<JournalLine> LineOfAsync(Guid player, int itemId) =>
        (await _journal.GetAsync(player)).Single(l => l.ItemId == itemId);

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
}

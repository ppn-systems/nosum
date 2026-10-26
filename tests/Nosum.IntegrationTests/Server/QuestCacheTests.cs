// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Quests;
using Nosum.Contracts.Quest;
using Nosum.Domain.Accounts;
using Nosum.Domain.Quests;
using Nosum.Gameplay.Quests;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Quests;
using Nosum.IntegrationTests.Server.DbBudget;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>The in-memory quests of a player (docs/adr/ADR-035): reads and reports that change nothing cost no command, and the copy always equals the rows.</summary>
public sealed class QuestCacheTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly DbCommandCounter _counter = new();
    private readonly EfUnitOfWorkFactory _units;
    private readonly TransferService _transfers;
    private readonly QuestService _quests;

    public QuestCacheTests(PostgresFixture database)
    {
        _database = database;
        DbContextOptions<NosumDbContext> options = new DbContextOptionsBuilder<NosumDbContext>()
            .UseNpgsql(database.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(_counter)
            .Options;
        _units = new EfUnitOfWorkFactory(options);
        _transfers = new TransferService(new InventoryService(_units, _clock), _clock);
        _quests = new QuestService(_units, _clock, null, _transfers);
    }

    [Fact]
    public async Task GetAsync_AskedAgain_CostsNoCommand()
    {
        Guid player = await CreatePlayerAsync();
        IReadOnlyList<QuestView> first = await _quests.GetAsync(player);

        int mark = _counter.Mark;
        IReadOnlyList<QuestView> second = await _quests.GetAsync(player);

        _counter.Since(mark).Commands.Should().Be(0);
        second.Select(q => (q.QuestId, q.State)).Should().Equal(first.Select(q => (q.QuestId, q.State)));
    }

    [Fact]
    public async Task ReportAsync_AnEventNoActiveQuestCounts_CostsNoCommand()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _quests.GetAsync(player);

        int mark = _counter.Mark;
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.NodeBroken, 99)); // nothing in the chain counts a node of this kind

        _counter.Since(mark).Commands.Should().Be(0, "a step that is not the current one is decided in memory");
        await AssertCacheEqualsRowsAsync(player);
    }

    [Fact]
    public async Task ReportAsync_ACountingEvent_WritesOnceAndKeepsTheCopyEqualToTheRows()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _quests.GetAsync(player);

        int mark = _counter.Mark;
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropPlanted, 1));

        DbCounts spent = _counter.Since(mark);
        spent.Reads.Should().Be(0);
        spent.Writes.Should().Be(1);
        await AssertCacheEqualsRowsAsync(player);
    }

    [Fact]
    public async Task ReportAsync_ACompletingEventWithAReward_PaysOnceAndTheCopyFollows()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _quests.GetAsync(player);
        long before = await _transfers.GetBalanceAsync(player);

        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.SoilTilled));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.SoilTilled));

        (await _quests.GetAsync(player)).Single(q => q.QuestId == QuestCatalog.TillSoil).State.Should().Be(QuestState.Completed);
        (await _transfers.GetBalanceAsync(player)).Should().Be(before + QuestCatalog.All.Single(q => q.Id == QuestCatalog.TillSoil).RewardCoins);
        await AssertCacheEqualsRowsAsync(player);
    }

    [Fact]
    public async Task ReportAsync_ParallelEventsOfOnePlayer_NeverLoseACountAndNeverPayTwice()
    {
        Guid player = await CreatePlayerAsync();
        long before = await _transfers.GetBalanceAsync(player);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropWatered))));
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => _quests.ReportAsync(player, new QuestEvent(QuestEventKind.SoilTilled))));

        await AssertCacheEqualsRowsAsync(player);
        await using NosumDbContext db = new(_database.Options);
        (await db.CoinLog.CountAsync(e => e.PlayerId == player && e.Reason == "quest.reward")).Should().Be(2, "one reward for each of the two steps");
        (await _transfers.GetBalanceAsync(player)).Should().Be(before + QuestCatalog.All.Where(q => q.Id is QuestCatalog.TillSoil or QuestCatalog.WaterCrop).Sum(q => q.RewardCoins));
    }

    [Fact]
    public async Task ReportAsync_ARowGoneBehindTheCopy_ThrowsAndTheNextCallReloads()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _quests.GetAsync(player);
        await using (NosumDbContext db = new(_database.Options))
        {
            _ = await db.PlayerQuests.Where(q => q.PlayerId == player).ExecuteDeleteAsync();
        }

        Func<Task> report = () => _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropPlanted, 1));
        _ = await report.Should().ThrowAsync<DbUpdateException>();

        IReadOnlyList<QuestView> reloaded = await _quests.GetAsync(player);
        reloaded.Should().HaveCount(QuestCatalog.All.Count).And.OnlyContain(q => q.State == QuestState.Active);
        await AssertCacheEqualsRowsAsync(player);
    }

    private async Task AssertCacheEqualsRowsAsync(Guid player)
    {
        IReadOnlyList<QuestView> cached = await _quests.GetAsync(player);
        IReadOnlyList<QuestView> rows = await new QuestService(_units, _clock).GetAsync(player);
        cached.Select(q => (q.QuestId, q.State, string.Join(',', q.Progress))).Should().Equal(rows.Select(q => (q.QuestId, q.State, string.Join(',', q.Progress))));
    }

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

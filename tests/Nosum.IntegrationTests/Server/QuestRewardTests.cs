// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Quests;
using Nosum.Contracts.Quest;
using Nosum.Contracts.Quest.S2C;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Quests;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Quests;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The coins a tutorial step pays (docs/adr/ADR-036 D2) against a real PostgreSQL database: completing the quest and paying it are one transaction, so a step pays
/// exactly once however often or however concurrently its event arrives.
/// </summary>
public sealed class QuestRewardTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly int[] s_oneStep = [1];
    private static readonly int[] s_twoSteps = [1, 1];

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly EfUnitOfWorkFactory _units;
    private readonly TransferService _transfers;
    private readonly RecordingNotifier _notifier = new();
    private readonly QuestService _quests;

    public QuestRewardTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _transfers = new TransferService(new InventoryService(_units, _clock), _clock);
        _quests = new QuestService(_units, _clock, _notifier, _transfers);
    }

    [Fact]
    public async Task TutorialStep_Completed_PaysItsRewardAndMarksTheQuestCompleted_InOneTransaction()
    {
        Guid player = await CreatePlayerAsync();
        long before = await _transfers.GetBalanceAsync(player);

        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.SoilTilled));

        (await _transfers.GetBalanceAsync(player)).Should().Be(before + Reward(QuestCatalog.TillSoil));
        QuestView quest = (await _quests.GetAsync(player)).Single(q => q.QuestId == QuestCatalog.TillSoil);
        quest.State.Should().Be(QuestState.Completed, "the quest is completed with the coins");
        quest.Progress.Should().Equal(s_oneStep);
        await using NosumDbContext context = new(_database.Options);
        (await context.CoinLog.Where(e => e.PlayerId == player && e.Reason == "quest.reward").ToListAsync()).Should().ContainSingle().Which.Delta.Should().Be(Reward(QuestCatalog.TillSoil));
        (await context.PlayerQuests.SingleAsync(q => q.PlayerId == player && q.QuestId == QuestCatalog.TillSoil)).CompletedAt.Should().Be(s_start);
        _notifier.Updates.Should().ContainSingle(u => u.QuestId == QuestCatalog.TillSoil && u.State == QuestState.Completed);
    }

    [Fact]
    public async Task TutorialStep_ReportedAgain_IsNotPaidTwice()
    {
        Guid player = await CreatePlayerAsync();
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropWatered));
        long afterFirst = await _transfers.GetBalanceAsync(player);

        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropWatered));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropWatered));

        (await _transfers.GetBalanceAsync(player)).Should().Be(afterFirst);
        await using NosumDbContext context = new(_database.Options);
        (await context.CoinLog.CountAsync(e => e.PlayerId == player && e.Reason == "quest.reward")).Should().Be(1);
    }

    [Fact]
    public async Task TutorialStep_ReportedByManyRequestsAtOnce_IsPaidOnce()
    {
        Guid player = await CreatePlayerAsync();
        long before = await _transfers.GetBalanceAsync(player);
        int completions = 0;
        _quests.Completed += completion =>
        {
            _ = completion;
            _ = Interlocked.Increment(ref completions);
            return ValueTask.CompletedTask;
        };

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => _quests.ReportAsync(player, new QuestEvent(QuestEventKind.SoilTilled)))));

        (await _transfers.GetBalanceAsync(player)).Should().Be(before + Reward(QuestCatalog.TillSoil));
        completions.Should().Be(1);
    }

    [Fact]
    public async Task TwoObjectiveQuest_PaysOnlyWhenTheLastObjectiveIsDone()
    {
        Guid player = await CreatePlayerAsync();
        long before = await _transfers.GetBalanceAsync(player);

        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropPlanted, 1));
        (await _transfers.GetBalanceAsync(player)).Should().Be(before, "planting alone completes nothing");
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropHarvested, 1));

        (await _transfers.GetBalanceAsync(player)).Should().Be(before + Reward(QuestCatalog.FirstHarvest));
    }

    [Fact]
    public async Task TheWholeChain_PaysTheSumOfItsRewards_InAnyOrder()
    {
        Guid player = await CreatePlayerAsync();
        long before = await _transfers.GetBalanceAsync(player);

        // Out of the chain's order on purpose: every active quest counts what it matches.
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.ItemCrafted, ItemCatalog.Chest, 1));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.NodeBroken, (int)NodeKind.Stone));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.NodeBroken, (int)NodeKind.Tree));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.SeedBought, ItemCatalog.ParsnipSeeds, 5));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.ItemShipped, ItemCatalog.Parsnip, 5));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropPlanted, 1));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropWatered));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.SoilTilled));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropHarvested, 1));

        (await _quests.GetAsync(player)).Should().OnlyContain(q => q.State == QuestState.Completed);
        (await _transfers.GetBalanceAsync(player)).Should().Be(before + QuestCatalog.All.Sum(q => q.RewardCoins));
    }

    [Fact]
    public async Task AQuestCompletedBeforeTheRewardsExisted_StaysCompletedAndIsNeverPaidLater()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _quests.GetAsync(player);
        await using (NosumDbContext context = new(_database.Options))
        {
            // As the table looked before this release: the first quest done, nothing else recorded about coins.
            _ = await context.PlayerQuests
                .Where(q => q.PlayerId == player && q.QuestId == QuestCatalog.FirstHarvest)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.State, QuestState.Completed).SetProperty(q => q.Progress, s_twoSteps).SetProperty(q => q.CompletedAt, s_start));
        }

        _quests.Forget(player); // rows changed behind the service's back: it reads them again (docs/adr/ADR-035)

        long before = await _transfers.GetBalanceAsync(player);
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropPlanted, 1));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropHarvested, 1));

        (await _transfers.GetBalanceAsync(player)).Should().Be(before);
    }

    [Fact]
    public async Task WithoutATransferService_TheQuestStillCompletes_AndPaysNothing()
    {
        Guid player = await CreatePlayerAsync();
        QuestService unpaid = new(_units, _clock);
        long before = await _transfers.GetBalanceAsync(player);

        await unpaid.ReportAsync(player, new QuestEvent(QuestEventKind.SoilTilled));

        (await unpaid.GetAsync(player)).Single(q => q.QuestId == QuestCatalog.TillSoil).State.Should().Be(QuestState.Completed);
        (await _transfers.GetBalanceAsync(player)).Should().Be(before);
    }

    private static long Reward(int questId)
    {
        _ = QuestCatalog.TryGet(questId, out QuestDefinition? quest);
        return quest!.RewardCoins;
    }

    private async Task<Guid> CreatePlayerAsync()
    {
        Player entity = TestNicknames.Name(new() { IsGuest = true, CreatedAt = s_start, LastActiveAt = s_start });
        await using NosumDbContext context = new(_database.Options);
        context.Players.Add(entity);
        _ = await context.SaveChangesAsync();
        return entity.Id;
    }

    private sealed class RecordingNotifier : IPlayerNotifier
    {
        public List<(Guid Player, int QuestId, QuestState State)> Updates { get; } = [];

        public ValueTask<bool> TrySendAsync(Guid playerId, IPacket packet, CancellationToken cancellationToken = default)
        {
            if (packet is QuestUpdate update)
            {
                lock (this.Updates)
                {
                    this.Updates.Add((playerId, update.Quest.QuestId, update.Quest.State));
                }
            }

            return ValueTask.FromResult(true);
        }
    }
}

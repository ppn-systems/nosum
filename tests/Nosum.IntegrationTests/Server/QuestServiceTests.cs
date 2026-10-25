// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Quests;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Quest;
using Nosum.Contracts.Quest.S2C;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Quests;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Quests;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>The quest service against a real PostgreSQL database: progress, completion once, pushes and the registration invitation.</summary>
public sealed class QuestServiceTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly RecordingNotifier _notifier = new();
    private readonly EfUnitOfWorkFactory _units;
    private readonly QuestService _quests;

    public QuestServiceTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _quests = new QuestService(_units, _clock, _notifier);
    }

    [Fact]
    public async Task GetAsync_NewPlayer_StartsTheWholeTutorialChain()
    {
        Guid player = await CreatePlayerAsync(guest: true);

        IReadOnlyList<QuestView> quests = await _quests.GetAsync(player);

        quests.Select(q => q.QuestId).Should().Equal(QuestCatalog.All.Select(q => q.Id).Order());
        quests.Should().OnlyContain(q => q.State == QuestState.Active);
        quests.Single(q => q.QuestId == QuestCatalog.FirstHarvest).Progress.Should().Equal(0, 0);
        quests.Single(q => q.QuestId == QuestCatalog.TillSoil).Progress.Should().Equal(0);
    }

    [Fact]
    public async Task ReportAsync_CountsMatchingEventsPersistsAndPushesEachChange()
    {
        Guid player = await CreatePlayerAsync(guest: true);

        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropPlanted, CropCatalog.Parsnip));

        IReadOnlyList<QuestView> quests = await new QuestService(_units, _clock).GetAsync(player);
        quests.Single(q => q.QuestId == QuestCatalog.FirstHarvest).Progress.Should().Equal(1, 0);
        (Guid to, int questId, QuestState state, int[] progress) = _notifier.Quests.Should().ContainSingle().Subject;
        (to, questId, state).Should().Be((player, QuestCatalog.FirstHarvest, QuestState.Active));
        progress.Should().Equal(1, 0);
    }

    [Fact]
    public async Task ReportAsync_BothObjectives_CompletesTheQuestOnceAndRaisesTheEvent()
    {
        Guid player = await CreatePlayerAsync(guest: true);
        List<QuestCompletion> completions = [];
        _quests.Completed += completion =>
        {
            completions.Add(completion);
            return ValueTask.CompletedTask;
        };

        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropPlanted, CropCatalog.Radish));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropHarvested, CropCatalog.Radish));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropHarvested, CropCatalog.Radish));

        completions.Should().Equal(new QuestCompletion(player, QuestCatalog.FirstHarvest));
        QuestView quest = (await _quests.GetAsync(player)).Single(q => q.QuestId == QuestCatalog.FirstHarvest);
        quest.State.Should().Be(QuestState.Completed);
        await using NosumDbContext context = new(_database.Options);
        (await context.PlayerQuests.SingleAsync(q => q.PlayerId == player && q.QuestId == QuestCatalog.FirstHarvest)).CompletedAt.Should().Be(s_start);
    }

    [Fact]
    public async Task ReportAsync_UnrelatedEvent_ChangesNothing()
    {
        Guid player = await CreatePlayerAsync(guest: true);
        _ = await _quests.GetAsync(player);

        await _quests.ReportAsync(player, new QuestEvent((QuestEventKind)99, 0));

        _notifier.Quests.Should().BeEmpty();
        (await _quests.GetAsync(player)).Single(q => q.QuestId == QuestCatalog.FirstHarvest).Progress.Should().Equal(0, 0);
    }

    [Fact]
    public async Task ReportAsync_AFailingCompletionHandler_DoesNotUndoOrBlockTheQuest()
    {
        Guid player = await CreatePlayerAsync(guest: true);
        _quests.Completed += _ => throw new InvalidOperationException("boom");

        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropPlanted));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropHarvested));

        (await _quests.GetAsync(player)).Single(q => q.QuestId == QuestCatalog.FirstHarvest).State.Should().Be(QuestState.Completed);
    }

    [Fact]
    public async Task CompletingTheFirstQuest_AsAGuest_UnlocksTheRegistrationPromptAndTellsTheClient()
    {
        Guid player = await CreatePlayerAsync(guest: true);
        PlayerLifecycleService prompts = new(_units, _clock);
        using RegistrationPromptOnQuestCompleted hook = new(_quests, prompts, _notifier);

        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropPlanted));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropHarvested));

        await using NosumDbContext context = new(_database.Options);
        (await context.Players.SingleAsync(p => p.Id == player)).RegistrationPromptUnlockedAt.Should().Be(s_start);
        _notifier.PromptUnlocked.Should().Equal(player);
    }

    [Fact]
    public async Task CompletingTheFirstQuest_AsARegisteredPlayer_UnlocksNothing()
    {
        Guid player = await CreatePlayerAsync(guest: false);
        PlayerLifecycleService prompts = new(_units, _clock);
        using RegistrationPromptOnQuestCompleted hook = new(_quests, prompts, _notifier);

        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropPlanted));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.CropHarvested));

        await using NosumDbContext context = new(_database.Options);
        (await context.Players.SingleAsync(p => p.Id == player)).RegistrationPromptUnlockedAt.Should().BeNull();
        _notifier.PromptUnlocked.Should().BeEmpty();
    }

    private async Task<Guid> CreatePlayerAsync(bool guest)
    {
        IUnitOfWork unit = _units.Create();
        await using (unit)
        {
            Player player = TestNicknames.Name(new() { IsGuest = guest, CreatedAt = s_start, LastActiveAt = s_start });
            unit.Repository<Player>().Add(player);
            _ = await unit.SaveChangesAsync(CancellationToken.None);
            return player.Id;
        }
    }

    private sealed class RecordingNotifier : IPlayerNotifier
    {
        public List<(Guid Player, int QuestId, QuestState State, int[] Progress)> Quests { get; } = [];

        public List<Guid> PromptUnlocked { get; } = [];

        public ValueTask<bool> TrySendAsync(Guid playerId, IPacket packet, CancellationToken cancellationToken = default)
        {
            switch (packet)
            {
                case QuestUpdate update:
                    this.Quests.Add((playerId, update.Quest.QuestId, update.Quest.State, update.Quest.Progress.ToArray()));
                    break;
                case RegistrationPromptUnlocked:
                    this.PromptUnlocked.Add(playerId);
                    break;
            }

            return ValueTask.FromResult(true);
        }
    }
}

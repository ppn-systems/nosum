// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

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
using Nosum.Abstractions.Community;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Mail;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Goals;
using Nosum.Contracts.Goals.S2C;
using Nosum.Domain.Accounts;
using Nosum.Domain.Community;
using Nosum.Domain.Economy;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Mail;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Infrastructure.Community;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Mail;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Community goals against a real PostgreSQL database (docs/adr/ADR-030): deliveries move items and counters in one transaction, concurrent deliveries add up exactly,
/// completion sets the unlock and pays once, and a season that ends expires the open goals with a consolation. The calendar starts in winter, whose goal asks for 1 000 parsnips.
/// </summary>
public sealed class CommunityGoalServiceTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const int WinterGoal = CommunityGoalCatalog.WinterLanterns;

    // Year 1, winter, game day 5: the calendar's epoch is three seasons (84 game days) and a bit before the clock.
    private static readonly DateTimeOffset s_now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly GameCalendar _calendar;
    private readonly FakeTimeProvider _clock = new(s_now);
    private readonly EfUnitOfWorkFactory _units;
    private readonly InventoryService _inventory;
    private readonly FlakyTransfers _transfers;
    private readonly RecordingPresence _presence = new();
    private readonly CommunityUnlocks _unlocks;
    private readonly CommunityGoalService _goals;
    private readonly MailService _mail;
    private readonly IConnection _connection = DispatchProxy.Create<IConnection, ConnectionStub>();

    public CommunityGoalServiceTests(PostgresFixture database)
    {
        _database = database;
        _calendar = new GameCalendar(s_now.ToUnixTimeMilliseconds() - ((GameCalendar.DaysPerSeason * 3L + 4) * GameCalendar.DefaultDayLengthMs), GameCalendar.DefaultDayLengthMs);
        _units = new EfUnitOfWorkFactory(database.Options);
        _inventory = new InventoryService(_units, _clock);
        _transfers = new FlakyTransfers(new TransferService(_inventory, _clock));
        _unlocks = new CommunityUnlocks(_units);
        _mail = new MailService(_units, _transfers, new NoMailPush(), _clock);
        _goals = new CommunityGoalService(_transfers, _units, _presence, new TownCatalog(), _calendar, _clock, _unlocks, _mail);
    }

    // Every test starts from an empty set of goals: a completed goal is permanent, so tests must not see each other's.
    public async Task InitializeAsync()
    {
        await using NosumDbContext context = new(_database.Options);
        _ = await context.CommunityContributions.ExecuteDeleteAsync();
        _ = await context.CommunityGoals.ExecuteDeleteAsync();
        _ = await context.CommunityUnlocks.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void TheClockOfTheseTests_IsInWinter()
    {
        _calendar.SeasonAt(_clock.GetUtcNow().ToUnixTimeMilliseconds()).Should().Be(Season.Winter);
        _calendar.SeasonNumberAt(_clock.GetUtcNow().ToUnixTimeMilliseconds()).Should().Be(3);
    }

    [Fact]
    public async Task Deliver_AtTheBoard_TakesTheItemsAndCountsThemForGoalAndPlayer_WithAuditRows()
    {
        Guid player = await NewPlayerAtBoardAsync(parsnips: 120);

        DeliverOutcome outcome = await _goals.DeliverAsync(_connection, player, WinterGoal, 100);

        outcome.Should().Be(new DeliverOutcome(GoalStatus.Ok, 100));
        (await ParsnipsAsync(player)).Should().Be(20);
        GoalView view = (await _goals.GetAsync(player)).Goals.Single(g => g.GoalId == WinterGoal);
        view.Should().Be(new GoalView(WinterGoal, 3, 100, GoalState.Active, 100, 1));
        await using NosumDbContext context = new(_database.Options);
        (await context.InventoryLog.Where(e => e.PlayerId == player && e.Reason == "community.deliver").ToListAsync()).Should().ContainSingle().Which.Delta.Should().Be(-100);
    }

    [Fact]
    public async Task Deliver_RepeatedByOnePlayer_AddsToTheirSingleContributionRow()
    {
        Guid player = await NewPlayerAtBoardAsync(parsnips: 90);

        (await _goals.DeliverAsync(_connection, player, WinterGoal, 30)).Status.Should().Be(GoalStatus.Ok);
        (await _goals.DeliverAsync(_connection, player, WinterGoal, 45)).Status.Should().Be(GoalStatus.Ok);

        GoalView view = (await _goals.GetAsync(player)).Goals.Single(g => g.GoalId == WinterGoal);
        (view.Total, view.Mine, view.Contributors).Should().Be((75L, 75L, 1));
        await using NosumDbContext context = new(_database.Options);
        (await context.CommunityContributions.CountAsync(c => c.PlayerId == player)).Should().Be(1);
    }

    [Fact]
    public async Task Deliver_NotAtTheBoard_IsRefusedAndNothingMoves()
    {
        Guid player = await NewPlayerAtBoardAsync(parsnips: 50);
        _presence.StandAt(player, 43, 57); // the seed shop's doormat, next to the board

        (await _goals.DeliverAsync(_connection, player, WinterGoal, 10)).Should().Be(new DeliverOutcome(GoalStatus.NotAtBoard, 0));

        _presence.StandAt(player, 10, 10);
        (await _goals.DeliverAsync(_connection, player, WinterGoal, 10)).Status.Should().Be(GoalStatus.NotAtBoard);
        _presence.Leave();
        (await _goals.DeliverAsync(_connection, player, WinterGoal, 10)).Status.Should().Be(GoalStatus.NotAtBoard, "not on any map");
        (await ParsnipsAsync(player)).Should().Be(50);
    }

    [Theory]
    [InlineData(WinterGoal, 0, GoalStatus.InvalidQuantity)]
    [InlineData(WinterGoal, -3, GoalStatus.InvalidQuantity)]
    [InlineData(WinterGoal, CommunityGoalRules.MaxDeliveryPerRequest + 1, GoalStatus.InvalidQuantity)]
    [InlineData(99, 5, GoalStatus.UnknownGoal)]
    [InlineData(CommunityGoalCatalog.SpringBridge, 5, GoalStatus.NotThisSeason)]
    [InlineData(CommunityGoalCatalog.SummerFestival, 5, GoalStatus.NotThisSeason)]
    public async Task Deliver_InvalidOrOutOfSeasonRequest_IsRefusedBeforeAnythingMoves(int goalId, int quantity, GoalStatus expected)
    {
        Guid player = await NewPlayerAtBoardAsync(parsnips: 50);

        (await _goals.DeliverAsync(_connection, player, goalId, quantity)).Should().Be(new DeliverOutcome(expected, 0));

        (await ParsnipsAsync(player)).Should().Be(50);
    }

    [Fact]
    public async Task Deliver_MoreThanThePlayerHas_IsRefusedAndNothingIsCounted()
    {
        Guid player = await NewPlayerAtBoardAsync(parsnips: 10);

        (await _goals.DeliverAsync(_connection, player, WinterGoal, 11)).Should().Be(new DeliverOutcome(GoalStatus.NotEnough, 0));

        (await ParsnipsAsync(player)).Should().Be(10);
        (await _goals.GetAsync(player)).Goals.Single(g => g.GoalId == WinterGoal).Total.Should().Be(0);
        await using NosumDbContext context = new(_database.Options);
        (await context.CommunityContributions.AnyAsync(c => c.PlayerId == player)).Should().BeFalse("a refused delivery leaves no row behind");
    }

    [Fact]
    public async Task Deliver_PushesTheNewTotalToEveryoneInTheTown()
    {
        Guid player = await NewPlayerAtBoardAsync(parsnips: 60);

        _ = await _goals.DeliverAsync(_connection, player, WinterGoal, 25);
        _ = await _goals.DeliverAsync(_connection, player, WinterGoal, 10);

        _presence.Pushes.Should().HaveCount(2);
        _presence.Pushes.Should().OnlyContain(p => p.Map == GameMaps.Town);
        _presence.Pushes.Select(p => (p.GoalId, p.Total, p.State)).Should().Equal((WinterGoal, 25L, GoalState.Active), (WinterGoal, 35L, GoalState.Active));
        _presence.Pushes.Select(p => p.Contributors).Should().OnlyContain(c => c == 1);
    }

    [Fact]
    public async Task Deliver_TwoPlayersAtTheSameTime_TotalsAreExactAndNothingIsLostOrCreated()
    {
        Guid a = await NewPlayerAtBoardAsync(parsnips: 200);
        Guid b = await NewPlayerAtBoardAsync(parsnips: 200);

        DeliverOutcome[] results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => _goals.DeliverAsync(_connection, a, WinterGoal, 7))
                .Concat(Enumerable.Range(0, 20).Select(_ => _goals.DeliverAsync(_connection, b, WinterGoal, 5))));

        results.Should().OnlyContain(r => r.Status == GoalStatus.Ok);
        GoalView view = (await _goals.GetAsync(a)).Goals.Single(g => g.GoalId == WinterGoal);
        view.Total.Should().Be((20 * 7) + (20 * 5));
        view.Mine.Should().Be(140);
        (await _goals.GetAsync(b)).Goals.Single(g => g.GoalId == WinterGoal).Mine.Should().Be(100);
        view.Contributors.Should().Be(2);
        (await ParsnipsAsync(a)).Should().Be(200 - 140);
        (await ParsnipsAsync(b)).Should().Be(200 - 100);
    }

    [Fact]
    public async Task Deliver_MoreThanTheGoalStillTakes_OnlyTheRemainderIsTaken_AndTheGoalCompletesOnce()
    {
        Guid a = await NewPlayerAtBoardAsync(parsnips: 990);
        Guid b = await NewPlayerAtBoardAsync(parsnips: 50);

        (await _goals.DeliverAsync(_connection, a, WinterGoal, 990)).Should().Be(new DeliverOutcome(GoalStatus.Ok, 990));
        DeliverOutcome last = await _goals.DeliverAsync(_connection, b, WinterGoal, 50);

        last.Should().Be(new DeliverOutcome(GoalStatus.Ok, 10), "only 10 were missing");
        (await ParsnipsAsync(b)).Should().Be(40);
        (await _goals.DeliverAsync(_connection, b, WinterGoal, 1)).Should().Be(new DeliverOutcome(GoalStatus.Closed, 0));
        (await ParsnipsAsync(b)).Should().Be(40);
        GoalView view = (await _goals.GetAsync(a)).Goals.Single(g => g.GoalId == WinterGoal);
        view.Should().Be(new GoalView(WinterGoal, 3, 1_000, GoalState.Completed, 990, 2));
    }

    [Fact]
    public async Task Completion_UnderConcurrentDeliveries_StopsExactlyAtTheTarget_SetsTheUnlockOnce_AndPaysEachContributorOnce()
    {
        Guid[] players = [await NewPlayerAtBoardAsync(300), await NewPlayerAtBoardAsync(300), await NewPlayerAtBoardAsync(300), await NewPlayerAtBoardAsync(300)];
        _unlocks.IsUnlocked(CommunityUnlockKeys.TownLanterns).Should().BeFalse();

        // 1 200 offered in parallel, the goal takes 1 000.
        DeliverOutcome[] results = await Task.WhenAll(players.SelectMany(p => Enumerable.Range(0, 30).Select(_ => _goals.DeliverAsync(_connection, p, WinterGoal, 10))));

        results.Where(r => r.Status == GoalStatus.Ok).Sum(r => r.Delivered).Should().Be(1_000);
        results.Where(r => r.Status != GoalStatus.Ok).Select(r => r.Status).Should().OnlyContain(s => s == GoalStatus.Closed);
        long taken = 0;
        foreach (Guid p in players)
        {
            taken += 300 - await ParsnipsAsync(p);
        }

        taken.Should().Be(1_000, "every counted item left exactly one inventory");
        await using NosumDbContext context = new(_database.Options);
        CommunityGoalProgress row = await context.CommunityGoals.SingleAsync(g => g.GoalId == WinterGoal);
        (row.Total, row.Status).Should().Be((1_000L, GoalStanding.Completed));
        (await context.CommunityContributions.Where(c => c.ProgressId == row.Id).SumAsync(c => c.Delivered)).Should().Be(1_000);
        (await context.CommunityUnlocks.Where(u => u.Key == CommunityUnlockKeys.TownLanterns).ToListAsync()).Should().ContainSingle().Which.GoalId.Should().Be(WinterGoal);
        _unlocks.IsUnlocked(CommunityUnlockKeys.TownLanterns).Should().BeTrue();
        _presence.Pushes.Last().State.Should().Be(GoalState.Completed);

        List<CommunityContribution> contributions = await context.CommunityContributions.Where(c => c.ProgressId == row.Id).ToListAsync();
        contributions.Should().OnlyContain(c => c.PaidCoins != null);
        foreach (CommunityContribution c in contributions)
        {
            (await context.CoinLog.CountAsync(e => e.PlayerId == c.PlayerId && e.Reason == "community.reward")).Should().Be(1, "paid exactly once");
        }
    }

    [Fact]
    public async Task Completion_PaysEachContributorTheirShareOfThePool_CappedPerPlayer()
    {
        Guid big = await NewPlayerAtBoardAsync(850);
        Guid small = await NewPlayerAtBoardAsync(50);
        Guid middle = await NewPlayerAtBoardAsync(100);
        _ = await _goals.DeliverAsync(_connection, big, WinterGoal, 850);
        _ = await _goals.DeliverAsync(_connection, small, WinterGoal, 50);
        _ = await _goals.DeliverAsync(_connection, middle, WinterGoal, 100);

        CommunityGoalCatalog.TryGet(WinterGoal, out CommunityGoalDefinition? goal);
        long start = Nosum.Gameplay.Economy.CoinRules.StartingCoins;
        (await _transfers.GetBalanceAsync(big)).Should().Be(start + 1_000, "85 % of the 10 000 pool is capped at 1 000");
        (await _transfers.GetBalanceAsync(small)).Should().Be(start + 500, "5 % of the pool");
        (await _transfers.GetBalanceAsync(middle)).Should().Be(start + 1_000, "10 % of the pool is exactly the cap");
        (await CoinLogOfAsync(big, "community.reward")).Should().ContainSingle().Which.Delta.Should().Be(1_000);
        goal!.CoinCapPerPlayer.Should().Be(1_000);
    }

    [Fact]
    public async Task Completion_SendsEachContributorANoticeOfWhatTheyWerePaid_WithNothingToCollect()
    {
        Guid big = await NewPlayerAtBoardAsync(850);
        Guid small = await NewPlayerAtBoardAsync(150);
        _ = await _goals.DeliverAsync(_connection, big, WinterGoal, 850);
        _ = await _goals.DeliverAsync(_connection, small, WinterGoal, 150);

        IReadOnlyList<MailView> mails = await _mail.ListAsync(small);
        mails.Should().ContainSingle(m => m.Code == MailRules.Codes.GoalReward);
        MailView notice = mails.Single(m => m.Code == MailRules.Codes.GoalReward);
        notice.Args.Should().Equal("1000");
        (await _mail.CollectAsync(small, notice.Id)).Should().NotBe(Nosum.Contracts.Mail.MailStatus.Ok, "the coins are already in the purse; nothing rides on the mail");
        (await _transfers.GetBalanceAsync(small)).Should().Be(Nosum.Gameplay.Economy.CoinRules.StartingCoins + 1_000);
    }

    [Fact]
    public async Task Completion_SettlingAgainAndInParallel_NeverPaysTwice()
    {
        Guid a = await NewPlayerAtBoardAsync(600);
        Guid b = await NewPlayerAtBoardAsync(400);
        _ = await _goals.DeliverAsync(_connection, a, WinterGoal, 600);
        _ = await _goals.DeliverAsync(_connection, b, WinterGoal, 400);
        long balanceA = await _transfers.GetBalanceAsync(a);
        long balanceB = await _transfers.GetBalanceAsync(b);

        _ = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => _goals.SettlePendingAsync()));
        _ = await _goals.CloseEndedSeasonsAsync();
        _ = await _goals.SettlePendingAsync();

        (await _transfers.GetBalanceAsync(a)).Should().Be(balanceA);
        (await _transfers.GetBalanceAsync(b)).Should().Be(balanceB);
        (await CoinLogOfAsync(a, "community.reward")).Should().HaveCount(1);
        (await CoinLogOfAsync(b, "community.reward")).Should().HaveCount(1);
    }

    [Fact]
    public async Task Completion_WhenPayingFailed_TheUnlockStands_AndTheNextSweepPaysOnce()
    {
        Guid a = await NewPlayerAtBoardAsync(700);
        Guid b = await NewPlayerAtBoardAsync(300);
        _transfers.FailPayouts = true;
        _ = await _goals.DeliverAsync(_connection, a, WinterGoal, 700);
        (await _goals.DeliverAsync(_connection, b, WinterGoal, 300)).Status.Should().Be(GoalStatus.Ok);

        _unlocks.IsUnlocked(CommunityUnlockKeys.TownLanterns).Should().BeTrue("the unlock does not wait for the coins");
        long before = await _transfers.GetBalanceAsync(a);
        await using (NosumDbContext context = new(_database.Options))
        {
            (await context.CommunityContributions.CountAsync(c => c.PaidCoins == null)).Should().Be(2);
        }

        _transfers.FailPayouts = false;
        (await _goals.SettlePendingAsync()).Should().Be(2);
        (await _goals.SettlePendingAsync()).Should().Be(0);

        (await _transfers.GetBalanceAsync(a)).Should().Be(before + 1_000);
        (await CoinLogOfAsync(b, "community.reward")).Should().ContainSingle().Which.Delta.Should().Be(1_000);
    }

    [Fact]
    public async Task Completion_IsPermanent_AFreshInstanceReadsTheUnlockFromTheDatabase_AndTheGoalStaysClosedInTheNextYear()
    {
        Guid a = await NewPlayerAtBoardAsync(1_000);
        _ = await _goals.DeliverAsync(_connection, a, WinterGoal, 1_000);

        new CommunityUnlocks(_units).IsUnlocked(CommunityUnlockKeys.TownLanterns).Should().BeTrue("a restarted server loads the flag from the table");
        new CommunityUnlocks(_units).IsUnlocked(CommunityUnlockKeys.BeachBridge).Should().BeFalse();

        // A year later, winter again: the goal is still complete and takes nothing.
        _clock.Advance(TimeSpan.FromMilliseconds(_calendar.SeasonLengthMs * 4));
        _calendar.SeasonAt(_clock.GetUtcNow().ToUnixTimeMilliseconds()).Should().Be(Season.Winter);
        (await _goals.CloseEndedSeasonsAsync()).Should().Be(0, "a completed goal never expires");
        Guid late = await NewPlayerAtBoardAsync(10);
        (await _goals.DeliverAsync(_connection, late, WinterGoal, 5)).Should().Be(new DeliverOutcome(GoalStatus.Closed, 0));
        (await _goals.GetAsync(late)).Goals.Single(g => g.GoalId == WinterGoal).State.Should().Be(GoalState.Completed);
    }

    [Fact]
    public async Task SeasonEnd_ExpiresTheUnfinishedGoal_AndPaysHalfThePricePerDeliveredItemOnce()
    {
        Guid a = await NewPlayerAtBoardAsync(300);
        Guid b = await NewPlayerAtBoardAsync(40);
        _ = await _goals.DeliverAsync(_connection, a, WinterGoal, 300);
        _ = await _goals.DeliverAsync(_connection, b, WinterGoal, 40);
        long balanceA = await _transfers.GetBalanceAsync(a);

        // Still winter: nothing happens.
        _clock.Advance(TimeSpan.FromMilliseconds(_calendar.SeasonLengthMs / 2));
        (await _goals.CloseEndedSeasonsAsync()).Should().Be(0);

        // The season turned: spring begins, winter's goal ran out.
        _clock.Advance(TimeSpan.FromMilliseconds(_calendar.SeasonLengthMs));
        _calendar.SeasonAt(_clock.GetUtcNow().ToUnixTimeMilliseconds()).Should().Be(Season.Spring);
        (await _goals.CloseEndedSeasonsAsync()).Should().Be(1);
        (await _goals.CloseEndedSeasonsAsync()).Should().Be(0);
        _ = await _goals.SettlePendingAsync();

        // Parsnips are 20 coins at the bin: 10 per delivered item.
        (await _transfers.GetBalanceAsync(a)).Should().Be(balanceA + 3_000);
        (await CoinLogOfAsync(a, "community.consolation")).Should().ContainSingle().Which.Delta.Should().Be(3_000);
        (await CoinLogOfAsync(b, "community.consolation")).Should().ContainSingle().Which.Delta.Should().Be(400);
        (await CoinLogOfAsync(a, "community.reward")).Should().BeEmpty();
        _unlocks.IsUnlocked(CommunityUnlockKeys.TownLanterns).Should().BeFalse("an expired goal unlocks nothing");
        _presence.Pushes.Last().State.Should().Be(GoalState.Expired);
        await using NosumDbContext context = new(_database.Options);
        (await context.CommunityGoals.SingleAsync(g => g.GoalId == WinterGoal)).Status.Should().Be(GoalStanding.Expired);
    }

    [Fact]
    public async Task SeasonEnd_TheExpiredGoalTakesNoMoreItems_AndNextYearsWinterStartsFromZero()
    {
        Guid a = await NewPlayerAtBoardAsync(100);
        _ = await _goals.DeliverAsync(_connection, a, WinterGoal, 60);
        _clock.Advance(TimeSpan.FromMilliseconds(_calendar.SeasonLengthMs));
        _ = await _goals.CloseEndedSeasonsAsync();

        // Spring: the winter goal is not this season's.
        (await _goals.DeliverAsync(_connection, a, WinterGoal, 10)).Should().Be(new DeliverOutcome(GoalStatus.NotThisSeason, 0));
        GoalsView spring = await _goals.GetAsync(a);
        spring.Goals.Select(g => g.GoalId).Should().Equal(CommunityGoalCatalog.SpringBridge);
        spring.Goals[0].Should().Be(new GoalView(CommunityGoalCatalog.SpringBridge, 4, 0, GoalState.Active, 0, 0));

        // The next winter: a new row for a new season number; the old contribution does not carry over.
        _clock.Advance(TimeSpan.FromMilliseconds(_calendar.SeasonLengthMs * 3));
        (await _goals.DeliverAsync(_connection, a, WinterGoal, 10)).Status.Should().Be(GoalStatus.Ok);
        (await _goals.GetAsync(a)).Goals.Single(g => g.GoalId == WinterGoal).Should().Be(new GoalView(WinterGoal, 7, 10, GoalState.Active, 10, 1));
    }

    [Fact]
    public async Task SeasonEnd_ADeliveryJustBeforeTheTurnAndTheSweepAtTheSameTime_NeverCountsAnItemTwiceOrLosesOne()
    {
        Guid a = await NewPlayerAtBoardAsync(500);
        _ = await _goals.DeliverAsync(_connection, a, WinterGoal, 100);
        long balance = await _transfers.GetBalanceAsync(a);
        _clock.Advance(TimeSpan.FromMilliseconds(_calendar.SeasonLengthMs));

        _ = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _goals.CloseEndedSeasonsAsync()));

        (await _transfers.GetBalanceAsync(a)).Should().Be(balance + 1_000, "one consolation of 100 items x 10 coins");
        (await CoinLogOfAsync(a, "community.consolation")).Should().HaveCount(1);
    }

    [Fact]
    public async Task Get_ListsTheSeasonsGoalsWithTheViewersPart_AndTheSeasonEnd()
    {
        Guid a = await NewPlayerAtBoardAsync(80);
        Guid b = await NewPlayerAtBoardAsync(40);
        _ = await _goals.DeliverAsync(_connection, a, WinterGoal, 80);
        _ = await _goals.DeliverAsync(_connection, b, WinterGoal, 15);

        GoalsView viewA = await _goals.GetAsync(a);
        GoalsView viewB = await _goals.GetAsync(b);
        GoalsView stranger = await _goals.GetAsync(Guid.NewGuid());

        viewA.ServerNowMs.Should().Be(_clock.GetUtcNow().ToUnixTimeMilliseconds());
        viewA.SeasonEndsAtMs.Should().Be(_calendar.NextSeasonStartMs(viewA.ServerNowMs));
        viewA.Goals.Should().ContainSingle().Which.Should().Be(new GoalView(WinterGoal, 3, 95, GoalState.Active, 80, 2));
        viewB.Goals.Single().Mine.Should().Be(15);
        stranger.Goals.Single().Mine.Should().Be(0);
    }

    // -- helpers --

    private static MapSpec TownSpec()
    {
        _ = GameMaps.TryGet(GameMaps.Town, out MapSpec? town);
        return town!;
    }

    private async Task<Guid> NewPlayerAtBoardAsync(int parsnips)
    {
        Player entity = TestNicknames.Name(new() { IsGuest = true, CreatedAt = s_now, LastActiveAt = s_now });
        await using (NosumDbContext context = new(_database.Options))
        {
            context.Players.Add(entity);
            _ = await context.SaveChangesAsync();
        }

        if (parsnips > 0)
        {
            _ = await _inventory.AddAsync(entity.Id, ItemCatalog.Parsnip, parsnips, "test.setup");
        }

        _presence.StandAt(entity.Id, CommunityGoalTile.X, CommunityGoalTile.Y);
        return entity.Id;
    }

    private async Task<int> ParsnipsAsync(Guid player)
        => (await _inventory.GetAsync(player)).Slots.Where(s => s.ItemId == ItemCatalog.Parsnip).Sum(s => s.Quantity);

    private async Task<List<CoinLogEntry>> CoinLogOfAsync(Guid player, string reason)
    {
        await using NosumDbContext context = new(_database.Options);
        return await context.CoinLog.Where(e => e.PlayerId == player && e.Reason == reason).ToListAsync();
    }

    private static (int X, int Y) CommunityGoalTile
    {
        get
        {
            ShopPoint board = TownSpec().Shops.Single(p => p.ShopId == CommunityGoalCatalog.BoardCounterId);
            return (board.TileX, board.TileY);
        }
    }

    // Pays like the real thing, unless told to fail the reward and consolation transfers (a crash between completion and payment).
    private sealed class FlakyTransfers : ITransferService
    {
        private readonly ITransferService _inner;

        public FlakyTransfers(ITransferService inner) => _inner = inner;

        public bool FailPayouts { get; set; }

        public Task<TransferStatus> ExecuteAsync(IReadOnlyList<TransferLeg> legs, string reason, TransferEnlistment? enlist = null, CancellationToken cancellationToken = default)
            => this.FailPayouts && reason.StartsWith("community.", StringComparison.Ordinal) && reason != "community.deliver"
                ? Task.FromResult(TransferStatus.Conflict)
                : _inner.ExecuteAsync(legs, reason, enlist, cancellationToken);

        public Task<long> GetBalanceAsync(Guid playerId, CancellationToken cancellationToken = default) => _inner.GetBalanceAsync(playerId, cancellationToken);
    }

    private class ConnectionStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod is { ReturnType: { IsValueType: true } type } && type != typeof(void) ? Activator.CreateInstance(type) : null;
    }

    private sealed class TownCatalog : IMapCatalog
    {
        public bool TryGet(string mapId, [NotNullWhen(true)] out MapDefinition? map)
        {
            // Only the tile size matters to the board; every tile of the blank grid is walkable.
            map = new MapDefinition(mapId, new WalkGrid(130, 110, 16, new bool[130 * 110]), 0, 0);
            return true;
        }
    }

    /// <summary>Reports one player at a chosen tile of the Town and records every progress push.</summary>
    private sealed class RecordingPresence : IMapPresence
    {
        private PlayerPresence? _player;

        public List<(string Map, int GoalId, long Total, GoalState State, int Contributors)> Pushes { get; } = [];

        public event Func<PlayerPresence, ValueTask>? PlayerLeft
        {
            add { }
            remove { }
        }

        public int PlayerCount => _player is null ? 0 : 1;

        [SuppressMessage("Reliability", "CA2000", Justification = "A stub connection that holds nothing to dispose.")]
        public void StandAt(Guid account, int tileX, int tileY)
            => _player = new PlayerPresence(1, account, new MapInstance(GameMaps.Town, Guid.Empty), DispatchProxy.Create<IConnection, ConnectionStub>(), new MotionState((tileX * 16) + 8, (tileY * 16) + 8, 0));

        public void Leave() => _player = null;

        public ValueTask<PlayerPresence> JoinAsync(IConnection connection, Guid accountId, MapInstance instance, MotionState motion, uint? playerId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask LeaveAsync(IConnection connection, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

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

        public ValueTask SendAsync(IConnection connection, IPacket packet, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask MulticastAsync(string mapId, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default)
        {
            // The packet goes back to its pool after this call: copy what the tests read.
            if (packet is GoalProgress progress)
            {
                lock (this.Pushes)
                {
                    this.Pushes.Add((mapId, progress.GoalId, progress.Total, progress.State, progress.Contributors));
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoMailPush : IPlayerNotifier
    {
        public ValueTask<bool> TrySendAsync(Guid playerId, Nalix.Abstractions.Networking.Packets.IPacket packet, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }
}

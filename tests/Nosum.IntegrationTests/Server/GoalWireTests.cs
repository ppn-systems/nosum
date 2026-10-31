// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nosum.Abstractions.Calendar;
using Nosum.Abstractions.Community;
using Nosum.Contracts.Goals;
using Nosum.Contracts.Goals.S2C;
using Nosum.Contracts.Market;
using Nosum.Contracts.Market.S2C;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The community goals and the price history through the real host over a WebSocket (docs/adr/ADR-030): deliveries at the Town board, the push to everyone in the Town,
/// the season end driven by the development clock, and the market limit and history packets.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class GoalWireTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(120);
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;

    public GoalWireTests(PostgresFixture database) => _database = database;

    // The tests share one database and goals are permanent: every test starts without goal rows or sales.
    public async Task InitializeAsync()
    {
        await using NosumDbContext context = new(_database.Options);
        _ = await context.CommunityContributions.ExecuteDeleteAsync();
        _ = await context.CommunityGoals.ExecuteDeleteAsync();
        _ = await context.CommunityUnlocks.ExecuteDeleteAsync();
        _ = await context.MarketSales.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Goals_DeliverOverTheWire_CountsTheItems_TellsEveryoneInTheTown_AndNeedsTheBoard()
    {
        await using Server server = await Server.StartAsync(_database);
        CancellationToken ct = server.Token;
        (string, double, double) board = BoardPlace();
        await using SocialClient deliverer = await SocialClient.ConnectAsync(server.Port, ct, board);
        await using SocialClient watcher = await SocialClient.ConnectAsync(server.Port, ct, board);
        await using SocialClient elsewhere = await SocialClient.ConnectAsync(server.Port, ct);
        await deliverer.JoinAsync(ct);
        await watcher.JoinAsync(ct);
        await elsewhere.JoinAsync(ct);
        watcher.ListenForGoalPushes();
        elsewhere.ListenForGoalPushes();

        // The development server counts from the last Monday: it is spring, and the spring goal is open.
        GoalsCopy before = await deliverer.GetGoalsAsync(ct);
        before.Rows.Should().ContainSingle().Which.Should().Match<GoalRow>(r => r.GoalId == CommunityGoalCatalog.SpringBridge && r.Total == 0 && r.State == GoalState.Active && r.Mine == 0);
        before.SeasonEndsAtMs.Should().BeGreaterThan(before.ServerNowMs);

        (await deliverer.GrantAsync(ItemCatalog.Parsnip, 60, 0, ct)).Should().BeTrue();
        (await deliverer.DeliverToGoalAsync(CommunityGoalCatalog.SpringBridge, 0, ct)).Status.Should().Be(GoalStatus.InvalidQuantity);
        (await deliverer.DeliverToGoalAsync(77, 5, ct)).Status.Should().Be(GoalStatus.UnknownGoal);
        (await deliverer.DeliverToGoalAsync(CommunityGoalCatalog.WinterLanterns, 5, ct)).Status.Should().Be(GoalStatus.NotThisSeason);
        (await deliverer.DeliverToGoalAsync(CommunityGoalCatalog.SpringBridge, 61, ct)).Status.Should().Be(GoalStatus.NotEnough);
        (await deliverer.CountAsync(ItemCatalog.Parsnip, ct)).Should().Be(60);

        (await deliverer.DeliverToGoalAsync(CommunityGoalCatalog.SpringBridge, 40, ct)).Should().Be((GoalStatus.Ok, 40));
        (await deliverer.DeliverToGoalAsync(CommunityGoalCatalog.SpringBridge, 5, ct)).Should().Be((GoalStatus.Ok, 5));

        // Everyone in the Town is told; someone on another map is not.
        GoalPushCopy push = await watcher.GoalPushes.WaitAsync(p => p.Total == 45, ct);
        push.Should().Be(new GoalPushCopy(CommunityGoalCatalog.SpringBridge, push.SeasonNumber, 45, GoalState.Active, 1));
        elsewhere.GoalPushCount.Should().Be(0);

        (await deliverer.CountAsync(ItemCatalog.Parsnip, ct)).Should().Be(15);
        GoalRow mine = (await deliverer.GetGoalsAsync(ct)).Rows.Single();
        (mine.Total, mine.Mine, mine.Contributors).Should().Be((45L, 45L, 1));
        GoalRow others = (await watcher.GetGoalsAsync(ct)).Rows.Single();
        (others.Total, others.Mine, others.Contributors).Should().Be((45L, 0L, 1));

        // Standing on the bus stop is not standing at the board.
        (await elsewhere.GrantAsync(ItemCatalog.Parsnip, 10, 0, ct)).Should().BeTrue();
        (await elsewhere.DeliverToGoalAsync(CommunityGoalCatalog.SpringBridge, 5, ct)).Should().Be((GoalStatus.NotAtBoard, 0));
        (await elsewhere.CountAsync(ItemCatalog.Parsnip, ct)).Should().Be(10);
    }

    [Fact]
    public async Task Goals_WhenTheSeasonEnds_TheOpenGoalExpires_TheTownIsToldAndTheContributorIsPaidTheConsolation()
    {
        await using Server server = await Server.StartAsync(_database);
        CancellationToken ct = server.Token;
        (string, double, double) board = BoardPlace();
        await using SocialClient deliverer = await SocialClient.ConnectAsync(server.Port, ct, board);
        await using SocialClient watcher = await SocialClient.ConnectAsync(server.Port, ct, board);
        await deliverer.JoinAsync(ct);
        await watcher.JoinAsync(ct);
        watcher.ListenForGoalPushes();
        (await deliverer.GrantAsync(ItemCatalog.Parsnip, 40, 0, ct)).Should().BeTrue();
        (await deliverer.DeliverToGoalAsync(CommunityGoalCatalog.SpringBridge, 40, ct)).Should().Be((GoalStatus.Ok, 40));
        (await deliverer.CoinsAsync(ct)).Should().Be(CoinRules.StartingCoins);

        // The development clock jumps past the end of spring (the server counted from the last Monday, about day 14 of 28).
        IDevClockService clock = InstanceManager.Instance.GetExistingInstance<IDevClockService>()!;
        clock.AdvanceDays(16).Should().NotBeNull();

        (await watcher.GoalPushes.WaitAsync(p => p.State == GoalState.Expired, ct)).Total.Should().Be(40);
        (await deliverer.Wallet.WaitAsync(c => c == CoinRules.StartingCoins + 800, ct)).Should().Be(CoinRules.StartingCoins + 800);
        GoalsCopy after = await deliverer.GetGoalsAsync(ct);
        after.Rows.Should().ContainSingle().Which.Should().Match<GoalRow>(r => r.GoalId == CommunityGoalCatalog.SummerFestival && r.Total == 0 && r.State == GoalState.Active);
        (await deliverer.DeliverToGoalAsync(CommunityGoalCatalog.SpringBridge, 1, ct)).Status.Should().Be(GoalStatus.NotThisSeason);
        InstanceManager.Instance.GetExistingInstance<ICommunityUnlocks>()!.IsUnlocked(CommunityUnlockKeys.BeachBridge).Should().BeFalse();
    }

    [Fact]
    public async Task Market_ListingMoreThanTheLimitOfOneItem_IsRefusedOverTheWire_AndASaleShowsInThePriceHistory()
    {
        await using Server server = await Server.StartAsync(_database);
        CancellationToken ct = server.Token;
        await using SocialClient seller = await SocialClient.ConnectAsync(server.Port, ct, MarketCounter.Place);
        await using SocialClient buyer = await SocialClient.ConnectAsync(server.Port, ct, MarketCounter.Place);
        await seller.JoinAsync(ct);
        await buyer.JoinAsync(ct);

        (await seller.GetPriceHistoryAsync(ItemCatalog.Parsnip, ct)).Days.Should().BeEmpty();
        for (int i = 0; i < 6; i++)
        {
            (await seller.GrantAsync(ItemCatalog.Parsnip, 99, 0, ct)).Should().BeTrue();
        }

        for (int i = 0; i < 5; i++)
        {
            (await seller.ListAsync(await seller.SlotOfAsync(ItemCatalog.Parsnip, ct), ItemCatalog.Parsnip, 99, 297, ct)).Should().Be(MarketStatus.Ok);
        }

        // 495 listed: five more fit, six do not.
        int slot = await seller.SlotOfAsync(ItemCatalog.Parsnip, ct);
        (await seller.ListAsync(slot, ItemCatalog.Parsnip, 6, 60, ct)).Should().Be(MarketStatus.TooManyOfItem);
        (await seller.ListAsync(slot, ItemCatalog.Parsnip, 5, 55, ct)).Should().Be(MarketStatus.Ok);
        (await seller.ListAsync(await seller.SlotOfAsync(ItemCatalog.Parsnip, ct), ItemCatalog.Parsnip, 1, 11, ct)).Should().Be(MarketStatus.TooManyOfItem);

        // The buyer takes the two cheapest-per-unit listings: 55 / 5 = 11 and 297 / 99 = 3 per unit.
        (int total, ListingRow[] rows) = await buyer.BrowseAsync([ItemCatalog.Parsnip], ct);
        total.Should().Be(6);
        (await buyer.BuyAsync(rows.Single(r => r.Quantity == 5).ListingId, ct)).Should().Be(MarketStatus.Ok);
        (await buyer.BuyAsync(rows.First(r => r.Quantity == 99).ListingId, ct)).Should().Be(MarketStatus.Ok);

        (int today, PriceDayRow[] days) = await buyer.GetPriceHistoryAsync(ItemCatalog.Parsnip, ct);
        PriceDayRow day = days.Should().ContainSingle().Subject;
        (day.Day, day.Min, day.Median, day.Max, day.Sales).Should().Be((today, 3L, 7L, 11L, 2), "units at 11 and 3 coins; the median of two is their mean");
    }

    private static (string Map, double X, double Y) BoardPlace()
    {
        _ = GameMaps.TryGet(GameMaps.Town, out MapSpec? town);
        ShopPoint board = town!.Shops.Single(p => p.ShopId == CommunityGoalCatalog.BoardCounterId);
        return (GameMaps.Town, (board.TileX * 16) + 8, (board.TileY * 16) + 8);
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    /// <summary>The real host on a free port in Development (the tester tools and the movable clock), with a token that expires so a stuck test fails instead of hanging.</summary>
    private sealed class Server : IAsyncDisposable
    {
        private readonly NetworkApplication _app;
        private readonly CancellationTokenSource _timeout = new(s_timeout);

        private Server(NetworkApplication app, ushort port)
        {
            _app = app;
            this.Port = port;
        }

        public ushort Port { get; }

        public CancellationToken Token => _timeout.Token;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The server is returned to the test, which disposes it.")]
        public static async Task<Server> StartAsync(PostgresFixture database)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);
            TestAccounts.UseFakeVerifiers(database.Options);
            NetworkApplication app = Program.BuildApplication(port, new FakeTimeProvider(s_start), development: true);
            Server server = new(app, port);
            await app.ActivateAsync(server.Token);
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DeactivateAsync(CancellationToken.None);
            await _app.DisposeAsync();
            _timeout.Dispose();
        }
    }
}

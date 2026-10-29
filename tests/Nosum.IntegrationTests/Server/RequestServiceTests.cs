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
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Requests;
using Nosum.Contracts.Requests;
using Nosum.Domain.Accounts;
using Nosum.Domain.Economy;
using Nosum.Domain.Requests;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Requests;
using Nosum.Gameplay.Shops;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Requests;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The Town requests board against a real PostgreSQL database (docs/adr/ADR-036): a day's requests are written once from the deterministic roll, a delivery takes the items and
/// pays the coins in one transaction, each player fills each request once a day, and only at the board.
/// </summary>
public sealed class RequestServiceTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    // Game day 10 of a calendar whose epoch is nine and a half days before this instant, so the day is 10, a spring day.
    private static readonly DateTimeOffset s_now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly GameCalendar _calendar;
    private readonly FakeTimeProvider _clock = new(s_now);
    private readonly EfUnitOfWorkFactory _units;
    private readonly InventoryService _inventory;
    private readonly TransferService _transfers;
    private readonly StandingPresence _presence = new();
    private readonly RequestService _requests;
    private readonly IConnection _connection = DispatchProxy.Create<IConnection, ConnectionStub>();

    public RequestServiceTests(PostgresFixture database)
    {
        _database = database;
        _calendar = new GameCalendar(s_now.ToUnixTimeMilliseconds() - (long)(9.5 * GameCalendar.DefaultDayLengthMs), GameCalendar.DefaultDayLengthMs);
        _units = new EfUnitOfWorkFactory(database.Options);
        _inventory = new InventoryService(_units, _clock);
        _transfers = new TransferService(_inventory, _clock);
        _requests = new RequestService(_transfers, _units, _presence, new TownCatalog(), _calendar, _clock);
    }

    // Every test starts from an empty board: the rows are keyed by day, and tests move the clock.
    public async Task InitializeAsync()
    {
        await using NosumDbContext context = new(_database.Options);
        _ = await context.RequestFills.ExecuteDeleteAsync();
        _ = await context.DailyRequests.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void TheClockOfTheseTests_IsGameDayTenInSpring()
    {
        long now = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        _calendar.DayNumberAt(now).Should().Be(10);
        _calendar.SeasonAt(now).Should().Be(Season.Spring);
    }

    [Fact]
    public async Task Get_WritesTheDaysRequestsOnce_AndTheyAreTheDeterministicRoll()
    {
        Guid player = await NewPlayerAsync();

        RequestsView first = await _requests.GetAsync(player);
        RequestsView second = await _requests.GetAsync(player);

        RequestTemplate[] roll = RequestRules.Roll(10, Season.Spring);
        first.Day.Should().Be(10);
        first.Requests.Select(r => (r.Slot, r.ItemId, r.Quantity, r.RewardCoins)).Should().Equal(roll.Select(r => (r.Slot, r.ItemId, r.Quantity, r.RewardCoins)));
        first.Requests.Should().OnlyContain(r => !r.Filled);
        second.Requests.Should().BeEquivalentTo(first.Requests);
        first.DayEndsAtMs.Should().Be(_calendar.NextDayStartMs(s_now.ToUnixTimeMilliseconds()));
        await using NosumDbContext context = new(_database.Options);
        (await context.DailyRequests.CountAsync(r => r.Day == 10)).Should().Be(RequestRules.RequestsPerDay);
    }

    [Fact]
    public async Task Get_ReportsWhenTheEarlyBonusEnds_AndItsPercent()
    {
        Guid player = await NewPlayerAsync();

        RequestsView view = await _requests.GetAsync(player);

        // Day 10 starts six real hours before the test clock; four in-game hours of a twelve-hour day are two of those real hours.
        view.EarlyUntilMs.Should().Be(_calendar.DayStartMs(10) + (2 * 60 * 60 * 1000));
        view.EarlyBonusPercent.Should().Be(RequestRules.EarlyBonusPercent);
    }

    [Fact]
    public async Task Get_EveryPlayerSeesTheSameRequests()
    {
        Guid a = await NewPlayerAsync();
        Guid b = await NewPlayerAsync();

        RequestsView forA = await _requests.GetAsync(a);
        RequestsView forB = await _requests.GetAsync(b);

        forB.Requests.Should().BeEquivalentTo(forA.Requests);
    }

    [Fact]
    public async Task EnsureDay_RunTwiceAtOnceFromTwoServices_WritesTheRowsOnce()
    {
        RequestService other = new(_transfers, _units, _presence, new TownCatalog(), _calendar, _clock);

        await Task.WhenAll(_requests.EnsureDayAsync(11), other.EnsureDayAsync(11), _requests.EnsureDayAsync(11));

        await using NosumDbContext context = new(_database.Options);
        (await context.DailyRequests.CountAsync(r => r.Day == 11)).Should().Be(RequestRules.RequestsPerDay);
    }

    [Fact]
    public async Task Deliver_AtTheBoard_TakesTheItemsAndPaysTheCoins_InOneTransfer()
    {
        Guid player = await NewPlayerAsync();
        RequestView request = (await _requests.GetAsync(player)).Requests[0];
        await GiveAsync(player, request.ItemId, request.Quantity + 3);
        long before = await _transfers.GetBalanceAsync(player);

        RequestOutcome outcome = await _requests.DeliverAsync(_connection, player, 10, request.Slot);

        outcome.Should().Be(new RequestOutcome(RequestStatus.Ok, request.RewardCoins));
        (await CountAsync(player, request.ItemId)).Should().Be(3, "only the requested quantity left the inventory");
        (await _transfers.GetBalanceAsync(player)).Should().Be(before + request.RewardCoins);
        await using NosumDbContext context = new(_database.Options);
        (await context.InventoryLog.Where(e => e.PlayerId == player && e.Reason == "request.deliver").ToListAsync()).Should().ContainSingle().Which.Delta.Should().Be(-request.Quantity);
        (await context.CoinLog.Where(e => e.PlayerId == player && e.Reason == "request.deliver").ToListAsync()).Should().ContainSingle().Which.Delta.Should().Be(request.RewardCoins);
        (await context.RequestFills.SingleAsync(f => f.PlayerId == player)).Should().Match<RequestFill>(f => f.Day == 10 && f.Slot == request.Slot && f.RewardCoins == request.RewardCoins);
        RequestsView view = await _requests.GetAsync(player);
        view.Requests.Where(r => r.Filled).Select(r => r.Slot).Should().Equal(request.Slot);
    }

    [Fact]
    public async Task Deliver_InTheEarlyWindow_PaysTheBonusOnTop_AndRecordsIt()
    {
        Guid player = await NewPlayerAsync();
        RequestView request = (await _requests.GetAsync(player)).Requests[0];
        await GiveAsync(player, request.ItemId, request.Quantity);
        _clock.Advance(TimeSpan.FromHours(-5)); // back to one real hour into game day 10, inside the early window
        long expected = request.RewardCoins * (100 + RequestRules.EarlyBonusPercent) / 100;

        RequestOutcome outcome = await _requests.DeliverAsync(_connection, player, 10, request.Slot);

        outcome.Should().Be(new RequestOutcome(RequestStatus.Ok, expected, Bonus: true));
        await using NosumDbContext context = new(_database.Options);
        (await context.RequestFills.SingleAsync(f => f.PlayerId == player)).RewardCoins.Should().Be(expected);
        (await context.CoinLog.Where(e => e.PlayerId == player && e.Reason == "request.deliver").ToListAsync()).Should().ContainSingle().Which.Delta.Should().Be(expected);
    }

    [Fact]
    public async Task Deliver_TheSameRequestTwiceInADay_IsRefusedAndNothingMoves()
    {
        Guid player = await NewPlayerAsync();
        RequestView request = (await _requests.GetAsync(player)).Requests[1];
        await GiveAsync(player, request.ItemId, request.Quantity * 2);
        (await _requests.DeliverAsync(_connection, player, 10, request.Slot)).Status.Should().Be(RequestStatus.Ok);
        long balance = await _transfers.GetBalanceAsync(player);

        RequestOutcome again = await _requests.DeliverAsync(_connection, player, 10, request.Slot);

        again.Should().Be(new RequestOutcome(RequestStatus.AlreadyFilled, 0));
        (await CountAsync(player, request.ItemId)).Should().Be(request.Quantity);
        (await _transfers.GetBalanceAsync(player)).Should().Be(balance);
    }

    [Fact]
    public async Task Deliver_AnotherRequestOrAnotherPlayer_IsStillOpen()
    {
        Guid a = await NewPlayerAsync();
        Guid b = await NewPlayerAsync();
        RequestView[] requests = [.. (await _requests.GetAsync(a)).Requests];
        await GiveAsync(a, requests[0].ItemId, requests[0].Quantity);
        await GiveAsync(a, requests[2].ItemId, requests[2].Quantity);
        await GiveAsync(b, requests[0].ItemId, requests[0].Quantity);

        (await _requests.DeliverAsync(_connection, a, 10, 0)).Status.Should().Be(RequestStatus.Ok);
        (await _requests.DeliverAsync(_connection, a, 10, 2)).Status.Should().Be(RequestStatus.Ok, "each request is its own once-a-day");
        (await _requests.DeliverAsync(_connection, b, 10, 0)).Status.Should().Be(RequestStatus.Ok, "the request is server-wide, the once is per player");
    }

    [Fact]
    public async Task Deliver_AwayFromTheBoard_IsRefusedAndNothingMoves()
    {
        Guid player = await NewPlayerAsync();
        RequestView request = (await _requests.GetAsync(player)).Requests[0];
        await GiveAsync(player, request.ItemId, request.Quantity);
        long balance = await _transfers.GetBalanceAsync(player);

        _presence.StandAt(player, 43, 57); // the seed shop's doormat, next to the board
        (await _requests.DeliverAsync(_connection, player, 10, request.Slot)).Should().Be(new RequestOutcome(RequestStatus.NotAtBoard, 0));
        _presence.StandAt(player, 10, 10);
        (await _requests.DeliverAsync(_connection, player, 10, request.Slot)).Status.Should().Be(RequestStatus.NotAtBoard);
        _presence.Leave();
        (await _requests.DeliverAsync(_connection, player, 10, request.Slot)).Status.Should().Be(RequestStatus.NotAtBoard, "not on any map");

        (await CountAsync(player, request.ItemId)).Should().Be(request.Quantity);
        (await _transfers.GetBalanceAsync(player)).Should().Be(balance);
        await using NosumDbContext context = new(_database.Options);
        (await context.RequestFills.CountAsync(f => f.PlayerId == player)).Should().Be(0);
    }

    [Fact]
    public async Task Deliver_WithoutEnoughItems_IsRefused_AndTheRequestStaysOpen()
    {
        Guid player = await NewPlayerAsync();
        RequestView request = (await _requests.GetAsync(player)).Requests[0];
        await GiveAsync(player, request.ItemId, request.Quantity - 1);

        (await _requests.DeliverAsync(_connection, player, 10, request.Slot)).Should().Be(new RequestOutcome(RequestStatus.NotEnough, 0));

        (await CountAsync(player, request.ItemId)).Should().Be(request.Quantity - 1);
        await GiveAsync(player, request.ItemId, 1);
        (await _requests.DeliverAsync(_connection, player, 10, request.Slot)).Status.Should().Be(RequestStatus.Ok);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public async Task Deliver_AnUnknownSlot_IsRefused(int slot)
    {
        Guid player = await NewPlayerAsync();
        _ = await _requests.GetAsync(player);

        (await _requests.DeliverAsync(_connection, player, 10, slot)).Status.Should().Be(RequestStatus.UnknownRequest);
    }

    [Fact]
    public async Task Deliver_ForAnotherDay_IsRefused()
    {
        Guid player = await NewPlayerAsync();
        RequestView request = (await _requests.GetAsync(player)).Requests[0];
        await GiveAsync(player, request.ItemId, request.Quantity);

        (await _requests.DeliverAsync(_connection, player, 9, request.Slot)).Status.Should().Be(RequestStatus.NotToday);
        (await _requests.DeliverAsync(_connection, player, 11, request.Slot)).Status.Should().Be(RequestStatus.NotToday);
        (await CountAsync(player, request.ItemId)).Should().Be(request.Quantity);
    }

    [Fact]
    public async Task ANewGameDay_BringsNewRequests_AndFillsDoNotCarryOver()
    {
        Guid player = await NewPlayerAsync();
        RequestView today = (await _requests.GetAsync(player)).Requests[0];
        await GiveAsync(player, today.ItemId, today.Quantity);
        (await _requests.DeliverAsync(_connection, player, 10, today.Slot)).Status.Should().Be(RequestStatus.Ok);

        _clock.Advance(TimeSpan.FromMilliseconds(GameCalendar.DefaultDayLengthMs));
        RequestsView tomorrow = await _requests.GetAsync(player);

        tomorrow.Day.Should().Be(11);
        tomorrow.Requests.Select(r => (r.Slot, r.ItemId, r.Quantity)).Should().Equal(RequestRules.Roll(11, Season.Spring).Select(r => (r.Slot, r.ItemId, r.Quantity)));
        tomorrow.Requests.Should().OnlyContain(r => !r.Filled, "a new day, a new once");
        RequestView next = tomorrow.Requests[0];
        await GiveAsync(player, next.ItemId, next.Quantity);
        (await _requests.DeliverAsync(_connection, player, 10, next.Slot)).Status.Should().Be(RequestStatus.NotToday, "yesterday's board is closed");
        (await _requests.DeliverAsync(_connection, player, 11, next.Slot)).Status.Should().Be(RequestStatus.Ok);
    }

    [Fact]
    public async Task EnsureDay_DeletesDaysOlderThanTheRetention_WithTheirFills()
    {
        Guid player = await NewPlayerAsync();
        RequestView request = (await _requests.GetAsync(player)).Requests[0];
        await GiveAsync(player, request.ItemId, request.Quantity);
        (await _requests.DeliverAsync(_connection, player, 10, request.Slot)).Status.Should().Be(RequestStatus.Ok);

        await _requests.EnsureDayAsync(10 + RequestService.RetentionDays + 1);

        await using NosumDbContext context = new(_database.Options);
        (await context.DailyRequests.CountAsync(r => r.Day == 10)).Should().Be(0);
        (await context.RequestFills.CountAsync(f => f.Day == 10)).Should().Be(0);
        (await context.DailyRequests.CountAsync(r => r.Day == 10 + RequestService.RetentionDays + 1)).Should().Be(RequestRules.RequestsPerDay);
    }

    [Fact]
    public async Task Deliver_ManyAtOnceByOnePlayer_FillsTheRequestExactlyOnce()
    {
        Guid player = await NewPlayerAsync();
        RequestView request = (await _requests.GetAsync(player)).Requests[0];
        await GiveAsync(player, request.ItemId, request.Quantity * 6);
        long before = await _transfers.GetBalanceAsync(player);

        RequestOutcome[] outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => _requests.DeliverAsync(_connection, player, 10, request.Slot))));

        outcomes.Count(o => o.Status == RequestStatus.Ok).Should().Be(1);
        outcomes.Where(o => o.Status != RequestStatus.Ok).Should().OnlyContain(o => o.Status == RequestStatus.AlreadyFilled);
        (await CountAsync(player, request.ItemId)).Should().Be(request.Quantity * 5);
        (await _transfers.GetBalanceAsync(player)).Should().Be(before + request.RewardCoins);
    }

    private async Task<Guid> NewPlayerAsync()
    {
        Player entity = TestNicknames.Name(new() { IsGuest = true, CreatedAt = s_now, LastActiveAt = s_now });
        await using (NosumDbContext context = new(_database.Options))
        {
            context.Players.Add(entity);
            _ = await context.SaveChangesAsync();
        }

        _presence.StandAt(entity.Id, BoardTile.X, BoardTile.Y);
        return entity.Id;
    }

    private async Task GiveAsync(Guid player, int itemId, int quantity)
    {
        if (quantity > 0)
        {
            _ = await _inventory.AddAsync(player, itemId, quantity, "test.setup");
        }
    }

    private async Task<int> CountAsync(Guid player, int itemId)
        => (await _inventory.GetAsync(player)).Slots.Where(s => s.ItemId == itemId).Sum(s => s.Quantity);

    private static (int X, int Y) BoardTile
    {
        get
        {
            _ = GameMaps.TryGet(GameMaps.Town, out MapSpec? town);
            ShopPoint board = town!.Shops.Single(p => p.ShopId == CommunityGoalCatalog.BoardCounterId);
            return (board.TileX, board.TileY);
        }
    }

    // The position of the one player the tests move; everything else a service asks of the presence is not needed.
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

        public ValueTask MulticastAsync(string mapId, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
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

    private class ConnectionStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod is { ReturnType: { IsValueType: true } type } && type != typeof(void) ? Activator.CreateInstance(type) : null;
    }
}

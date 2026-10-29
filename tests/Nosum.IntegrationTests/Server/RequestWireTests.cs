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
using Nalix.Hosting;
using Nosum.Contracts.Requests;
using Nosum.Contracts.Requests.S2C;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Requests;
using Nosum.Gameplay.Shops;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The requests board through the real host over a WebSocket (docs/adr/ADR-036): the handlers, the encrypted delivery, the service registration and the board counter.
/// The rules themselves are in <see cref="RequestServiceTests"/>.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class RequestWireTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(120);
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;

    public RequestWireTests(PostgresFixture database) => _database = database;

    // The tests share one database and the rows are keyed by day: every test starts with an empty board.
    public async Task InitializeAsync()
    {
        await using NosumDbContext context = new(_database.Options);
        _ = await context.RequestFills.ExecuteDeleteAsync();
        _ = await context.DailyRequests.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Requests_ReadAndDeliverOverTheWire_TakeTheItemsPayTheCoinsAndFillOncePerDay()
    {
        await using Server server = await Server.StartAsync(_database);
        CancellationToken ct = server.Token;
        await using SocialClient farmer = await SocialClient.ConnectAsync(server.Port, ct, BoardPlace());
        await using SocialClient elsewhere = await SocialClient.ConnectAsync(server.Port, ct);
        await farmer.JoinAsync(ct);
        await elsewhere.JoinAsync(ct);

        RequestsCopy board = await farmer.GetRequestsAsync(ct);
        board.Rows.Should().HaveCount(RequestRules.RequestsPerDay);
        board.Rows.Select(r => r.Slot).Should().Equal(0, 1, 2);
        board.Rows.Should().OnlyContain(r => !r.Filled && r.Quantity > 0 && r.RewardCoins > 0);
        board.DayEndsAtMs.Should().BeGreaterThan(board.ServerNowMs);
        RequestRow request = board.Rows[0];

        // Refusals first: nothing moves.
        (await farmer.DeliverRequestAsync(board.Day, 7, ct)).Status.Should().Be(RequestStatus.UnknownRequest);
        (await farmer.DeliverRequestAsync(board.Day - 1, request.Slot, ct)).Status.Should().Be(RequestStatus.NotToday);
        (await farmer.DeliverRequestAsync(board.Day, request.Slot, ct)).Status.Should().Be(RequestStatus.NotEnough);
        (await elsewhere.GrantAsync(request.ItemId, request.Quantity, 0, ct)).Should().BeTrue();
        (await elsewhere.DeliverRequestAsync(board.Day, request.Slot, ct)).Status.Should().Be(RequestStatus.NotAtBoard, "the bus stop is not the board");
        (await elsewhere.CountAsync(request.ItemId, ct)).Should().Be(request.Quantity);

        // At the board with the items: they are taken and the coins arrive. The board says whether the early bonus is open at its clock; the delivery is paid it then.
        bool early = board.EarlyUntilMs > board.ServerNowMs;
        long paid = RequestRules.PaidFor(request.RewardCoins, early);
        (await farmer.GrantAsync(request.ItemId, request.Quantity + 2, 0, ct)).Should().BeTrue();
        (await farmer.DeliverRequestAsync(board.Day, request.Slot, ct)).Should().Be((RequestStatus.Ok, paid, early));
        (await farmer.CountAsync(request.ItemId, ct)).Should().Be(2);
        (await farmer.CoinsAsync(ct)).Should().Be(CoinRules.StartingCoins + paid);

        // The second time on the same day is refused and the board says so.
        (await farmer.DeliverRequestAsync(board.Day, request.Slot, ct)).Should().Be((RequestStatus.AlreadyFilled, 0L, false));
        (await farmer.CountAsync(request.ItemId, ct)).Should().Be(2);
        (await farmer.GetRequestsAsync(ct)).Rows.Where(r => r.Filled).Select(r => r.Slot).Should().Equal(request.Slot);
        (await elsewhere.GetRequestsAsync(ct)).Rows.Should().OnlyContain(r => !r.Filled, "fills are per player");
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

    /// <summary>The real host on a free port in Development (the tester tools), with a token that expires so a stuck test fails instead of hanging.</summary>
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

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
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.S2C;
using Nosum.Contracts.Combat.C2S;
using Nosum.Contracts.Look;
using Nosum.Contracts.Look.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Movement;
using Nosum.Host;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Walking speed over real Nalix WebSocket sessions (docs/adr/ADR-084): boots worn on the feet raise the pace the server accepts, and a claim
/// above that pace is refused with a correction. The validator reads the server's real clock, and that clock keeps running under load, so the
/// outcome of a timed walk can only be decided with a bound: an accepted walk waits at least one step between claims (the budget then
/// refills at least as fast as the walk spends it), and a refusal is one claim further than any budget can hold (a refill never raises the
/// budget above its cap, so no timing lets it through).
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class FootwearSpeedWireTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(90);
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    // Each claim covers this much time; the pace is a multiple of the base walking speed.
    private static readonly TimeSpan s_step = TimeSpan.FromMilliseconds(250);
    private const int Steps = 12;

    private readonly PostgresFixture _database;

    public FootwearSpeedWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task WithoutBoots_AWalkFasterThanWalkingIsRefused()
    {
        await using Server server = await Server.StartAsync(_database);
        await using SocialClient player = await SocialClient.ConnectAsync(server.Port, server.Token);
        JoinMapResult join = await player.JoinAsync(server.Token);

        LookRow[] looks = await player.GetLooksAsync(server.Token);
        looks.Single(l => l.PlayerId == join.PlayerId).FeetItemId.Should().Be(0);

        // One claim further than the budget can hold: bare feet cannot cover it, however long the walk took.
        await OverCapAsync(server, player, join, running: false);
        (await CorrectionsAsync(player, server.Token)).Should().Contain(c => c.Reason == MoveRefusal.TooFast, "bare feet walk at the base speed");
    }

    [Fact]
    public async Task WithWingedBoots_TheServerAcceptsThePaceTheBootsGive()
    {
        await using Server server = await Server.StartAsync(_database);
        await using SocialClient player = await SocialClient.ConnectAsync(server.Port, server.Token);
        JoinMapResult join = await player.JoinAsync(server.Token);
        await WearAsync(player, ItemCatalog.WingedBoots, server.Token);

        // Winged boots give +20%: the walk at that pace is accepted every step.
        await WalkAsync(server, player, join, 1.2);
        (await CorrectionsAsync(player, server.Token)).Select(c => (c.Sequence, c.Reason, c.X)).Should().BeEmpty("the boots raise the walking pace the server allows");

        // The same connection still gets refusals: a jump far beyond any pace is corrected, so the empty list above means accepted, not dropped.
        await player.SendMoveAsync(Steps + 1, join.SpawnX + 400, join.SpawnY, server.Token);
        (await CorrectionsAsync(player, server.Token)).Should().Contain(c => c.Reason == MoveRefusal.TooFast);
    }

    [Fact]
    public async Task WithWingedBoots_APaceAboveTheBootsIsRefused()
    {
        await using Server server = await Server.StartAsync(_database);
        await using SocialClient player = await SocialClient.ConnectAsync(server.Port, server.Token);
        JoinMapResult join = await player.JoinAsync(server.Token);
        await WearAsync(player, ItemCatalog.WingedBoots, server.Token);

        // A claim beyond the boots' cap is refused, so the multiplier is the authority and the client's claim is not.
        await OverCapAsync(server, player, join, running: false);
        (await CorrectionsAsync(player, server.Token)).Should().Contain(c => c.Reason == MoveRefusal.TooFast);
    }

    [Fact]
    public async Task WithWingedBootsAndTheSpeedTonic_WalkingTopsOutAt1Point5()
    {
        await using Server server = await Server.StartAsync(_database);
        await using SocialClient player = await SocialClient.ConnectAsync(server.Port, server.Token);
        JoinMapResult join = await player.JoinAsync(server.Token);
        await WearAsync(player, ItemCatalog.WingedBoots, server.Token);

        // The tonic is a Swiftness draught (+25%): with winged boots (+20%) walking reaches 1.5, the highest walking stack. Only running goes past it.
        (await player.GrantAsync(ItemCatalog.SpeedTonic, 1, 0, server.Token)).Should().BeTrue();
        (await player.DrinkPotionAsync(await player.SlotOfAsync(ItemCatalog.SpeedTonic, server.Token), server.Token)).Should().Be(CombatStatus.Ok);

        await WalkAsync(server, player, join, 1.5);
        (await CorrectionsAsync(player, server.Token)).Should().BeEmpty("walking at the boosted stack is accepted");

        // Walking past the boosted cap (1.5) is refused: the run factor is what the cap is raised for.
        await OverCapAsync(server, player, join, running: false);
        (await CorrectionsAsync(player, server.Token)).Should().Contain(c => c.Reason == MoveRefusal.TooFast);
    }

    [Fact]
    public async Task WithWingedBoots_ARunAtTheCapIsAccepted()
    {
        await using Server server = await Server.StartAsync(_database);
        await using SocialClient player = await SocialClient.ConnectAsync(server.Port, server.Token);
        JoinMapResult join = await player.JoinAsync(server.Token);
        await WearAsync(player, ItemCatalog.WingedBoots, server.Token);

        // Winged boots (1.20) times the run factor (1.5) is 1.8, the cap: boots still matter while running, so the pace is accepted.
        await WalkAsync(server, player, join, MovementSpeed.MaxFactor, running: true);
        (await CorrectionsAsync(player, server.Token)).Should().BeEmpty("the boots and the run together give the cap");
    }

    [Fact]
    public async Task WithWingedBoots_ARunAboveTheCapIsRefused()
    {
        await using Server server = await Server.StartAsync(_database);
        await using SocialClient player = await SocialClient.ConnectAsync(server.Port, server.Token);
        JoinMapResult join = await player.JoinAsync(server.Token);
        await WearAsync(player, ItemCatalog.WingedBoots, server.Token);

        // Above the cap the server refuses a run, whatever the client claims.
        await OverCapAsync(server, player, join, running: true);
        (await CorrectionsAsync(player, server.Token)).Should().Contain(c => c.Reason == MoveRefusal.TooFast);
    }

    // One claim that covers more ground than any budget can hold: the budget never rises above its cap, which is MaxBudgetSeconds of walking at the
    // fastest pace (the running pace), so twice that is refused at every pace and every moment.
    private static Task OverCapAsync(Server server, SocialClient player, JoinMapResult join, bool running)
    {
        double distance = 2 * MoveValidator.MaxBudgetSeconds * Walker.Speed * MovementSpeed.MaxFactor;
        return player.SendMoveAsync(Steps + 1, join.SpawnX + (float)distance, join.SpawnY, server.Token, running);
    }

    // Walks back and forth between two points, each claim covering pace × walking speed × step seconds, in real time between claims.
    private static async Task WalkAsync(Server server, SocialClient player, JoinMapResult join, double pace, bool running = false)
    {
        double distance = pace * Walker.Speed * s_step.TotalSeconds;
        for (int step = 1; step <= Steps; step++)
        {
            float x = join.SpawnX + (step % 2 == 0 ? 0 : (float)distance);
            await player.SendMoveAsync((uint)step, x, join.SpawnY, server.Token, running);

            // At least one step of real time between claims: the validator refills the budget from the server's clock, so an accepted walk is
            // never starved of budget, however slowly the machine answers.
            await Task.Delay(s_step, server.Token);
        }
    }

    private static async Task WearAsync(SocialClient player, int itemId, CancellationToken ct)
    {
        (await player.GrantAsync(itemId, 1, 0, ct)).Should().BeTrue();
        int slot = await player.SlotOfAsync(itemId, ct);
        (await player.EquipAsync(slot, itemId, ct)).Should().Be(LookStatus.Ok);
    }

    // The refusals the server sent. A request answered on the same connection comes after the claims sent before it, so once it returns
    // their corrections are in the inbox; no fixed wait is needed.
    private static async Task<MoveCorrection[]> CorrectionsAsync(SocialClient player, CancellationToken ct)
    {
        _ = await player.GetInventoryAsync(ct);
        MoveCorrection[] seen = [];
        while (player.Corrections.TryTake(out MoveCorrection correction))
        {
            seen = [.. seen, correction];
        }

        return seen;
    }

    private static ushort GetFreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The server is returned to the test, which disposes it.")]
    private sealed class Server : IAsyncDisposable
    {
        private readonly NetworkApplication _app;
        private readonly CancellationTokenSource _timeout = new(s_timeout);

        private Server(NetworkApplication app, ushort port, FakeTimeProvider clock)
        {
            _app = app;
            this.Port = port;
            this.Clock = clock;
        }

        public ushort Port { get; }

        public FakeTimeProvider Clock { get; }

        public CancellationToken Token => _timeout.Token;

        public static async Task<Server> StartAsync(PostgresFixture database)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);
            TestAccounts.UseFakeVerifiers(database.Options);
            FakeTimeProvider clock = new(s_start);
            NetworkApplication app = Program.BuildApplication(port, clock, development: true);
            Server server = new(app, port, clock);
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

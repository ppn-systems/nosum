// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Maps;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Host;
using Xunit;
using Xunit.Abstractions;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Measures what the server sends per snapshot tick with 20 real WebSocket clients standing on Town in three layouts (docs/adr/ADR-022). The test is self-contained on
/// purpose: it uses only wire types that exist since ADR-008, so the very same file runs against the code before chunks for the "before" numbers. It asserts the invariants
/// that hold on both sides and prints the figures; the chunked server must additionally send fewer bytes than the all-to-all one.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class ChunkLoadTests : IClassFixture<PostgresFixture>
{
    private const int Players = 20;
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(120);

    private readonly PostgresFixture _database;
    private readonly ITestOutputHelper _output;

    public ChunkLoadTests(PostgresFixture database, ITestOutputHelper output)
    {
        _database = database;
        _output = output;
    }

    [Fact]
    public async Task TwentyPlayersOnTown_SpreadEvenlyOrRandomly_ReceiveFewerBytesPerTickThanWhenEverybodySeesEverybody()
    {
        ushort port = FreePort();
        HostServices.Register(_database.Options);
        TestAccounts.UseFakeVerifiers(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        List<LoadClient> clients = [];
        try
        {
            IMapCatalog catalog = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
            _ = catalog.TryGet(GameMaps.Town, out MapDefinition? town);
            List<(double X, double Y)> free = FreeSpots(town!);
            for (int i = 0; i < Players; i++)
            {
                clients.Add(await LoadClient.ConnectAsync(port, timeout.Token));
            }

            Dictionary<string, Result> results = [];
            foreach ((string name, List<(double X, double Y)> places) in new (string, List<(double X, double Y)>)[]
            {
                ("Even", Even(free, Players)),
                ("Random", Random(free, Players, 22)),
                ("Clustered", Clustered(free, Players)),
            })
            {
                results[name] = await MeasureAsync(clients, places, timeout.Token);
                _output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "LAYOUT={0,-9} players={1} ticks={2} bytesPerTick(all receivers)={3,8:F0} bytesPerReceiverPerTick={4,7:F1} entriesPerSnapshot={5,5:F2} joinBurstBytes={6}",
                    name, Players, results[name].Ticks, results[name].BytesPerTick, results[name].BytesPerReceiverPerTick, results[name].EntriesPerSnapshot, results[name].JoinBurstBytes));
            }

            // Everybody always sees themselves; nobody can see more than everybody.
            foreach (Result result in results.Values)
            {
                result.EntriesPerSnapshot.Should().BeGreaterThanOrEqualTo(1);
                result.EntriesPerSnapshot.Should().BeLessThanOrEqualTo(Players);
            }

            results["Clustered"].EntriesPerSnapshot.Should().BeApproximately(Players, 0.5, "players in one chunk see each other, before and after chunks");
            if (Environment.GetEnvironmentVariable("NOSUM_LOAD_BASELINE") != "1")
            {
                results["Even"].EntriesPerSnapshot.Should().BeLessThan(Players / 2.0, "spread over Town, a player sees only the neighbouring chunks");
                results["Random"].EntriesPerSnapshot.Should().BeLessThan(Players / 2.0);
                results["Even"].BytesPerTick.Should().BeLessThan(results["Clustered"].BytesPerTick / 2);
            }
        }
        finally
        {
            foreach (LoadClient client in clients)
            {
                await client.DisposeAsync();
            }

            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    private static async Task<Result> MeasureAsync(List<LoadClient> clients, List<(double X, double Y)> places, CancellationToken ct)
    {
        long burstStart = clients.Sum(c => c.Bytes);
        for (int i = 0; i < clients.Count; i++)
        {
            await clients[i].PutOnAsync(GameMaps.Town, places[i].X, places[i].Y);
            await clients[i].JoinAsync(ct);
            await Task.Delay(260, ct);
        }

        // Let the join traffic (spawns, chunk state) settle, then count a window of ticks.
        await Task.Delay(2500, ct);
        long burst = clients.Sum(c => c.Bytes) - burstStart;
        long bytes0 = clients.Sum(c => c.Bytes);
        long entries0 = clients.Sum(c => c.Entries);
        long snapshots0 = clients.Sum(c => (long)c.Snapshots);
        int ticks0 = clients[0].Snapshots;
        await Task.Delay(3000, ct);
        long bytes = clients.Sum(c => c.Bytes) - bytes0;
        long entries = clients.Sum(c => c.Entries) - entries0;
        long snapshots = clients.Sum(c => (long)c.Snapshots) - snapshots0;
        int ticks = clients[0].Snapshots - ticks0;
        return new Result(ticks, bytes / (double)ticks, bytes / (double)ticks / clients.Count, entries / (double)snapshots, burst);
    }

    private static List<(double X, double Y)> FreeSpots(MapDefinition map)
    {
        List<(double X, double Y)> spots = [];
        int tile = map.Grid.TileSize;
        for (int ty = 2; ty < map.Grid.Height - 2; ty++)
        {
            for (int tx = 2; tx < map.Grid.Width - 2; tx++)
            {
                double x = (tx + 0.5) * tile;
                double y = (ty + 0.5) * tile;
                if (!MoveValidator.FootBlocked(map.Grid, x, y)
                    && !MoveValidator.FootBlocked(map.Grid, x - tile, y)
                    && !MoveValidator.FootBlocked(map.Grid, x + tile, y)
                    && !WarpRule.IsOnExitTile(map.Warps, map.Grid, x, y))
                {
                    spots.Add((x, y));
                }
            }
        }

        return spots;
    }

    // Farthest-point sampling: each next player stands as far as possible from everybody placed so far.
    private static List<(double X, double Y)> Even(List<(double X, double Y)> free, int count)
    {
        List<(double X, double Y)> picked = [free[free.Count / 2]];
        double[] nearest = free.Select(s => Distance(s, picked[0])).ToArray();
        while (picked.Count < count)
        {
            int best = 0;
            for (int i = 1; i < free.Count; i++)
            {
                if (nearest[i] > nearest[best])
                {
                    best = i;
                }
            }

            picked.Add(free[best]);
            for (int i = 0; i < free.Count; i++)
            {
                nearest[i] = Math.Min(nearest[i], Distance(free[i], free[best]));
            }
        }

        return picked;
    }

    private static List<(double X, double Y)> Random(List<(double X, double Y)> free, int count, int seed)
    {
        Random random = new(seed);
        return Enumerable.Range(0, count).Select(_ => free[random.Next(free.Count)]).ToList();
    }

    // Everybody in the chunk that has the most free tiles.
    private static List<(double X, double Y)> Clustered(List<(double X, double Y)> free, int count)
    {
        var crowded = free.GroupBy(s => ((int)s.X / 16 / 16, (int)s.Y / 16 / 16)).OrderByDescending(g => g.Count()).First().ToList();
        return Enumerable.Range(0, count).Select(i => crowded[i % crowded.Count]).ToList();
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static ushort FreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private sealed record Result(int Ticks, double BytesPerTick, double BytesPerReceiverPerTick, double EntriesPerSnapshot, long JoinBurstBytes);

    private sealed class LoadClient : IAsyncDisposable
    {
        private static readonly SemaphoreSlim s_gate = new(1, 1);
        private static DateTime s_last = DateTime.MinValue;

        private readonly WebSocketSession _session;
        private readonly IDisposable _snapshots;
        private long _bytes;
        private long _entries;
        private int _count;

        private LoadClient(WebSocketSession session)
        {
            _session = session;
            _session.OnMessageReceived += (_, lease) => Interlocked.Add(ref _bytes, lease.Length);
            _snapshots = session.On<MapSnapshot>(s =>
            {
                _ = Interlocked.Add(ref _entries, s.Players.Count);
                _ = Interlocked.Increment(ref _count);
            });
        }

        public long Bytes => Interlocked.Read(ref _bytes);

        public long Entries => Interlocked.Read(ref _entries);

        public int Snapshots => Volatile.Read(ref _count);

        private Guid AccountId { get; set; }

        public static async Task<LoadClient> ConnectAsync(ushort port, CancellationToken ct)
        {
            WebSocketSession session = new(
                new TransportOptions { Address = "127.0.0.1", Port = port },
                new WebSocketTransportOptions { Path = Program.WebSocketPath });
            LoadClient client = new(session);
            await session.ConnectAsync(ct: ct);
            await session.HandshakeAsync(ct);
            IAccountService accounts = InstanceManager.Instance.GetExistingInstance<IAccountService>()!;
            AccountOutcome created = await accounts.StartGuestAsync(Nosum.Contracts.Common.GameLanguage.English, ct);
            created.Status.Should().Be(AccountStatus.Ok);

            // Resuming a session is limited to two a second per address and every client here shares one.
            await s_gate.WaitAsync(ct);
            try
            {
                TimeSpan wait = TimeSpan.FromMilliseconds(650) - (DateTime.UtcNow - s_last);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, ct);
                }

                s_last = DateTime.UtcNow;
            }
            finally
            {
                _ = s_gate.Release();
            }

            using ResumeSession request = new() { SessionToken = created.SessionToken! };
            AccountResult result = await session.RequestAsync<AccountResult>(request, ct: ct);
            result.Status.Should().Be(AccountStatus.Ok);
            client.AccountId = result.PlayerId;
            return client;
        }

        public async Task PutOnAsync(string mapId, double x, double y)
        {
            IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
            await states.SaveAsync(this.AccountId, new PlayerStateRow(mapId, x, y, Nosum.Core.Facing.Down), CancellationToken.None);
        }

        public async Task JoinAsync(CancellationToken ct)
        {
            using JoinMap request = new() { MapId = Program.FarmMapId };
            JoinMapResult result = await _session.RequestAsync<JoinMapResult>(request, ct: ct);
            result.Status.Should().Be(JoinStatus.Ok);
        }

        public async ValueTask DisposeAsync()
        {
            _snapshots.Dispose();
            await _session.DisconnectAsync();
            _session.Dispose();
        }
    }
}

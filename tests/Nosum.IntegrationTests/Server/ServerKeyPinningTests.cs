// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Hosting;
using Nalix.Runtime.Handlers;
using Nosum.Host;
using Nosum.Wasm.Socket;
using Nosum.Wasm.Socket.Security;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>The client's server key pinning and trust on first use (docs/adr/ADR-032 D1) against the real host.</summary>
[Collection(NalixHostSerial.Name)]
public sealed class ServerKeyPinningTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _database;
    private ushort _port;

    public ServerKeyPinningTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Connect_WithThePinnedKey_BecomesReady()
    {
        await using NetworkApplication app = await StartAsync();
        try
        {
            GameSocketOptions options = Options();
            options.ServerPublicKeys.Add(ServerKey());
            await using GameSocket socket = new(options);

            await ReachAsync(socket, GameSocketState.Ready);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Connect_WithAKeyThatIsNotTheServers_IsRefusedAndReportsAMismatch()
    {
        await using NetworkApplication app = await StartAsync();
        try
        {
            GameSocketOptions options = Options();
            options.ServerPublicKeys.Add(OtherKey());
            await using GameSocket socket = new(options);

            await ReachAsync(socket, GameSocketState.KeyMismatch);
            socket.State.Should().NotBe(GameSocketState.Ready);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Connect_DuringARotationWithTheOldAndTheNewKeyPinned_AcceptsTheKeyTheServerHolds()
    {
        await using NetworkApplication app = await StartAsync();
        try
        {
            GameSocketOptions options = Options();
            options.ServerPublicKeys.Add(OtherKey());
            options.ServerPublicKeys.Add(ServerKey());
            await using GameSocket socket = new(options);

            await ReachAsync(socket, GameSocketState.Ready);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Connect_TrustOnFirstUse_RemembersTheKeyAfterTheHandshake()
    {
        await using NetworkApplication app = await StartAsync();
        try
        {
            MemoryKeyStore store = new();
            GameSocketOptions options = Options();
            options.KeyStore = store;
            await using GameSocket socket = new(options);

            await ReachAsync(socket, GameSocketState.Ready);

            ServerKeys.AreSame(store.Stored, ServerKey()).Should().BeTrue("the key the server proved is remembered");
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Connect_TrustOnFirstUse_WhenTheKeyChangedSinceRefusesAndKeepsTheOldOne()
    {
        await using NetworkApplication app = await StartAsync();
        try
        {
            string remembered = OtherKey();
            MemoryKeyStore store = new() { Stored = remembered };
            GameSocketOptions options = Options();
            options.KeyStore = store;
            await using GameSocket socket = new(options);

            await ReachAsync(socket, GameSocketState.KeyMismatch);

            store.Stored.Should().Be(remembered, "a changed key is never trusted silently");
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Connect_WithoutAnyPinOrStore_StillTrustsTheServer()
    {
        await using NetworkApplication app = await StartAsync();
        try
        {
            await using GameSocket socket = new(Options());

            await ReachAsync(socket, GameSocketState.Ready);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void Constructor_PinRequiredButNoneConfiguredOrMalformed_FailsClosed()
    {
        GameSocketOptions required = new() { RequirePin = true };
        GameSocketOptions malformed = new();
        malformed.ServerPublicKeys.Add("not-a-key");
        GameSocketOptions tooMany = new();
        tooMany.ServerPublicKeys.Add(new string('A', 64));
        tooMany.ServerPublicKeys.Add(new string('B', 64));
        tooMany.ServerPublicKeys.Add(new string('C', 64));

        Func<GameSocket> a = () => new GameSocket(required);
        Func<GameSocket> b = () => new GameSocket(malformed);
        Func<GameSocket> c = () => new GameSocket(tooMany);

        _ = a.Should().Throw<FormatException>();
        _ = b.Should().Throw<FormatException>();
        _ = c.Should().Throw<FormatException>();
    }

    private async Task<NetworkApplication> StartAsync()
    {
        HostServices.Register(_database.Options);
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        _port = (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        NetworkApplication app = Program.BuildApplication(_port);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        await app.ActivateAsync(timeout.Token);
        return app;
    }

    private GameSocketOptions Options()
        => new()
        {
            Host = "127.0.0.1",
            Port = _port,
            ReconnectBaseDelay = TimeSpan.FromMilliseconds(50),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(200),
        };

    private static string ServerKey() => HandshakeHandlers.ServerPublicKey.ToString();

    private static string OtherKey()
    {
        byte[] bytes = HandshakeHandlers.ServerPublicKey.ToByteArray();
        bytes[0] ^= 0xFF;
        return Convert.ToHexString(bytes);
    }

    private static async Task ReachAsync(GameSocket socket, GameSocketState wanted)
    {
        socket.Start();
        Stopwatch clock = Stopwatch.StartNew();
        while (socket.State != wanted)
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(30))
            {
                throw new TimeoutException("The socket stayed " + socket.State + " instead of reaching " + wanted + ".");
            }

            await Task.Delay(50);
        }
    }

    private sealed class MemoryKeyStore : IServerKeyStore
    {
        public string? Stored { get; set; }

        public ValueTask<string?> GetAsync() => ValueTask.FromResult(this.Stored);

        public ValueTask SetAsync(string keyHex)
        {
            this.Stored = keyHex;
            return ValueTask.CompletedTask;
        }
    }
}

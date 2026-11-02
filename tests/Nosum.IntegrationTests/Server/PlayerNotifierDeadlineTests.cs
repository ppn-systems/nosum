// Copyright (c) 2026 PPN Systems. All rights reserved.
// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Accounts;
using Nosum.Contracts.Account;
using Nosum.Contracts.Player.S2C;
using Nosum.Infrastructure.Presence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// A single-recipient push (<see cref="PlayerNotifier.TrySendAsync"/>) has the same send deadline as a fan-out (docs/adr/ADR-087 D6, amendment
/// 2026-11-02, second): a player who does not take the packet in time is disconnected, and the handler that pushed to them does not wait on
/// them any longer.
/// </summary>
public sealed class PlayerNotifierDeadlineTests
{
    private static readonly TimeSpan s_deadline = TimeSpan.FromMilliseconds(100);

    // Generous bound for the wait: the send must return near the deadline, not after a transport timeout.
    private static readonly TimeSpan s_bound = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task TrySendAsync_StalledPlayer_ReturnsFalseAtTheDeadlineAndDisconnectsThem()
    {
        Recorder recorder = new(stall: true);
        PlayerNotifier notifier = new(new OnePlayer(recorder.Connection), recorder.Send) { SendDeadline = s_deadline, EncryptSends = false };
        using MapSnapshot packet = new();

        Stopwatch clock = Stopwatch.StartNew();
        bool sent = await notifier.TrySendAsync(Guid.NewGuid(), packet);
        clock.Stop();

        sent.Should().BeFalse("the player never took the packet");
        clock.Elapsed.Should().BeLessThan(s_bound, "the push must stop waiting at the deadline");
        recorder.Disconnects.Should().Be(1, "a player who misses the deadline is disconnected, as a fan-out recipient is");
    }

    [Fact]
    public async Task TrySendAsync_PlayerWhoTakesThePacket_ReturnsTrueAndStaysConnected()
    {
        Recorder recorder = new(stall: false);
        PlayerNotifier notifier = new(new OnePlayer(recorder.Connection), recorder.Send) { SendDeadline = s_deadline, EncryptSends = false };
        using MapSnapshot packet = new();

        bool sent = await notifier.TrySendAsync(Guid.NewGuid(), packet);

        sent.Should().BeTrue();
        recorder.Sends.Should().Be(1);
        recorder.Disconnects.Should().Be(0);
    }

    [Fact]
    public async Task TrySendAsync_CallerCancels_ReturnsFalseAndDoesNotDisconnect()
    {
        Recorder recorder = new(stall: true);
        PlayerNotifier notifier = new(new OnePlayer(recorder.Connection), recorder.Send) { SendDeadline = TimeSpan.FromMinutes(1), EncryptSends = false };
        using MapSnapshot packet = new();
        using CancellationTokenSource caller = new(TimeSpan.FromMilliseconds(50));

        bool sent = await notifier.TrySendAsync(Guid.NewGuid(), packet, caller.Token);

        sent.Should().BeFalse();
        recorder.Disconnects.Should().Be(0, "the caller's own cancellation is not the player's fault");
    }

    // Returns one connection for any player.
    private sealed class OnePlayer : ISessionRegistry
    {
        private readonly IConnection _connection;

        public OnePlayer(IConnection connection) => _connection = connection;

        public ValueTask ActivateAsync(Guid playerId, IConnection connection, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Release(IConnection connection)
        {
        }

        public ValueTask<bool> EndAsync(Guid playerId, AccountStatus reason, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

        public IConnection? GetActive(Guid playerId) => _connection;
    }

    // Stands in for the player's connection and its transport: the send either completes at once or waits for its token, and every send and disconnect is counted.
    public sealed class Recorder
    {
        private readonly bool _stall;
        private int _sends;
        private int _disconnects;

        public Recorder(bool stall)
        {
            _stall = stall;
            Connection = DispatchProxy.Create<IConnection, ConnectionProxy>();
            ((ConnectionProxy)(object)Connection).Recorder = this;
        }

        public IConnection Connection { get; }

        public int Sends => Volatile.Read(ref _sends);

        public int Disconnects => Volatile.Read(ref _disconnects);

        public async ValueTask Send(IConnection connection, IPacket packet, bool enableEncrypt, CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _sends);
            if (_stall)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
        }

        public void CountDisconnect() => _ = Interlocked.Increment(ref _disconnects);
    }

    // Only Disconnect is observed on the connection; the send itself goes through Recorder.Send.
    public class ConnectionProxy : DispatchProxy
    {
        public Recorder? Recorder { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is not null && targetMethod.Name == nameof(IConnection.Disconnect))
            {
                Recorder?.CountDisconnect();
            }

            return targetMethod is { ReturnType.IsValueType: true } && targetMethod.ReturnType != typeof(void)
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }
}

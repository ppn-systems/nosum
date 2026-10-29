// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Wasm.Socket;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class ConnectionStatusViewModelTests
{
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ThirtySeconds = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData("en", GameSocketState.Ready, "Online")]
    [InlineData("en", GameSocketState.Reconnecting, "Connection lost, retrying…")]
    [InlineData("vi", GameSocketState.Ready, "Trực tuyến")]
    [InlineData("vi", GameSocketState.Offline, "Ngoại tuyến")]
    [InlineData("en", GameSocketState.KeyMismatch, "The server identity has changed. It may be an impostor, so the game refuses to connect. Wait for an official announcement, or clear this site's data if you are running a development build.")]
    public void Text_ForStateAndCulture_ComesFromTheResources(string culture, GameSocketState state, string expected)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            ScriptedSocket socket = new() { State = state };
            using ConnectionStatusViewModel vm = new(socket, Localizer());

            vm.Text.Should().Be(expected);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Theory]
    [InlineData(GameSocketState.Ready, false)]
    [InlineData(GameSocketState.Connecting, false)]
    [InlineData(GameSocketState.Reconnecting, true)]
    [InlineData(GameSocketState.Offline, true)]
    [InlineData(GameSocketState.KeyMismatch, false)]
    public void IsOverlayVisible_ForEachState_IsUpOnlyWhileTheConnectionIsLost(GameSocketState state, bool expected)
    {
        ScriptedSocket socket = new() { State = state };
        using ConnectionStatusViewModel vm = new(socket, Localizer(), new ManualClock());

        vm.IsOverlayVisible.Should().Be(expected);
    }

    [Theory]
    [InlineData(GameSocketState.Ready, false)]
    [InlineData(GameSocketState.Connecting, true)]
    [InlineData(GameSocketState.Reconnecting, true)]
    [InlineData(GameSocketState.Offline, true)]
    [InlineData(GameSocketState.KeyMismatch, true)]
    public void BlocksInput_ForEachState_HoldsInputUntilTheConnectionIsReady(GameSocketState state, bool expected)
    {
        ScriptedSocket socket = new() { State = state };
        using ConnectionStatusViewModel vm = new(socket, Localizer(), new ManualClock());

        vm.BlocksInput.Should().Be(expected);
    }

    [Fact]
    public void SecondsUntilRetry_AfterAScheduledWait_CountsDownAndStopsWhenDue()
    {
        ManualClock clock = new();
        ScriptedSocket socket = new() { State = GameSocketState.Reconnecting };
        using ConnectionStatusViewModel vm = new(socket, Localizer(), clock);

        socket.ScheduleRetry(TimeSpan.FromSeconds(3));
        vm.SecondsUntilRetry.Should().Be(3);

        clock.Advance(TimeSpan.FromSeconds(1.5));
        vm.SecondsUntilRetry.Should().Be(2);

        clock.Advance(TimeSpan.FromSeconds(1.5));
        vm.SecondsUntilRetry.Should().Be(0);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(7, 30)]
    public void SecondsUntilRetry_AfterTheNthFailedAttempt_IsTheBackoffDelay(int failures, int expectedSeconds)
    {
        ScriptedSocket socket = new() { State = GameSocketState.Reconnecting };
        using ConnectionStatusViewModel vm = new(socket, Localizer(), new ManualClock());

        socket.ScheduleRetry(ReconnectBackoff.Delay(failures, OneSecond, ThirtySeconds));

        vm.SecondsUntilRetry.Should().Be(expectedSeconds);
    }

    [Fact]
    public void OverlayMessage_WhileWaiting_CountsDownInTheUiCulture()
    {
        ScriptedSocket socket = new() { State = GameSocketState.Reconnecting };
        using ConnectionStatusViewModel vm = new(socket, Localizer(), new ManualClock());
        socket.ScheduleRetry(TimeSpan.FromSeconds(3));

        string message = InCulture("en", () => vm.OverlayMessage);

        message.Should().Be("Reconnecting in 3 s…");
    }

    [Fact]
    public void OverlayMessage_WhenTheAttemptIsDue_SaysReconnecting()
    {
        ManualClock clock = new();
        ScriptedSocket socket = new() { State = GameSocketState.Reconnecting };
        using ConnectionStatusViewModel vm = new(socket, Localizer(), clock);
        socket.ScheduleRetry(OneSecond);
        clock.Advance(OneSecond);

        string message = InCulture("en", () => vm.OverlayMessage);

        message.Should().Be("Reconnecting…");
    }

    [Fact]
    public void OverlayMessage_WhenOffline_SaysTheServerIsNotReached()
    {
        ScriptedSocket socket = new() { State = GameSocketState.Offline };
        using ConnectionStatusViewModel vm = new(socket, Localizer(), new ManualClock());

        string title = InCulture("en", () => vm.OverlayTitle);
        string message = InCulture("en", () => vm.OverlayMessage);

        title.Should().Be("Offline");
        message.Should().Be("Not connected to the server.");
    }

    [Fact]
    public void OverlayTitle_WhenReconnecting_IsConnectionLost()
    {
        ScriptedSocket socket = new() { State = GameSocketState.Reconnecting };
        using ConnectionStatusViewModel vm = new(socket, Localizer(), new ManualClock());

        string title = InCulture("en", () => vm.OverlayTitle);

        title.Should().Be("Connection lost");
    }

    [Fact]
    public void RetryNow_AsksTheSocketToEndTheWait()
    {
        ScriptedSocket socket = new() { State = GameSocketState.Reconnecting };
        using ConnectionStatusViewModel vm = new(socket, Localizer(), new ManualClock());

        vm.RetryNow();

        socket.RetryNowCalls.Should().Be(1);
    }

    [Fact]
    public void Tick_WhileTheOverlayIsUp_RaisesChanged()
    {
        ScriptedSocket socket = new() { State = GameSocketState.Reconnecting };
        using ConnectionStatusViewModel vm = new(socket, Localizer(), new ManualClock());
        int changes = 0;
        vm.Changed += () => changes++;

        vm.Tick();

        changes.Should().Be(1);
    }

    [Fact]
    public void Tick_WhenConnected_RaisesNothing()
    {
        ScriptedSocket socket = new() { State = GameSocketState.Ready };
        using ConnectionStatusViewModel vm = new(socket, Localizer(), new ManualClock());
        int changes = 0;
        vm.Changed += () => changes++;

        vm.Tick();

        changes.Should().Be(0);
    }

    [Fact]
    public void Overlay_WhenTheConnectionComesBack_HidesAndForgetsTheCountdown()
    {
        ScriptedSocket socket = new() { State = GameSocketState.Reconnecting };
        using ConnectionStatusViewModel vm = new(socket, Localizer(), new ManualClock());
        socket.ScheduleRetry(TimeSpan.FromSeconds(5));

        socket.Move(GameSocketState.Ready);

        vm.IsOverlayVisible.Should().BeFalse();
        vm.BlocksInput.Should().BeFalse();
        vm.SecondsUntilRetry.Should().Be(0);
    }

    [Fact]
    public void Dispose_StopsFollowingTheSocket()
    {
        ScriptedSocket socket = new() { State = GameSocketState.Ready };
        ConnectionStatusViewModel vm = new(socket, Localizer(), new ManualClock());
        int changes = 0;
        vm.Changed += () => changes++;

        vm.Dispose();
        socket.Move(GameSocketState.Reconnecting);
        socket.ScheduleRetry(OneSecond);

        changes.Should().Be(0);
    }

    private static IStringLocalizer<Language> Localizer()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<Language>(factory);
    }

    private static T InCulture<T>(string culture, Func<T> read)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            return read();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>A clock the test moves by hand, so the countdown is deterministic.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>A socket the test moves by hand: it changes state, schedules waits and counts the retry requests.</summary>
    private sealed class ScriptedSocket : IGameSocket
    {
        public event Action<GameSocketState>? StateChanged;

        public event Action? Ready
        {
            add { }
            remove { }
        }

        public event Action<TimeSpan>? ReconnectScheduled;

        public GameSocketState State { get; set; }

        public int RetryNowCalls { get; private set; }

        /// <summary>Moves to <paramref name="state"/> and raises <see cref="StateChanged"/>, as the socket does.</summary>
        public void Move(GameSocketState state)
        {
            this.State = state;
            this.StateChanged?.Invoke(state);
        }

        /// <summary>Announces a wait before the next attempt, as the socket does.</summary>
        public void ScheduleRetry(TimeSpan delay) => this.ReconnectScheduled?.Invoke(delay);

        public void Start() => throw new NotSupportedException();

        public void RetryNow() => this.RetryNowCalls++;

        public Task WaitReadyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask<TResponse> RequestAsync<TResponse>(IPacket request, CancellationToken cancellationToken = default)
            where TResponse : class, IPacket, IPacketStaticOpcode => throw new NotSupportedException();

        public ValueTask SendAsync(IPacket packet, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IDisposable Subscribe<TPacket>(Action<TPacket> handler)
            where TPacket : class, IPacket, IPacketStaticOpcode => throw new NotSupportedException();
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Codec.Pooling;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Chat;
using Nosum.Contracts.Chat.C2S;
using Nosum.Contracts.Chat.S2C;
using Nosum.Contracts.Common;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.World.S2C;

namespace Nosum.Bench.Load;

/// <summary>
/// One simulated player (docs/adr/ADR-031 D8): connects like the browser client does (handshake, guest, join), reports its position 10 times a second
/// (the <see cref="Navigator"/> decides where it is), and either walks through the exits to a public map (a traveller) or stays on its farm and
/// uses its tools (a farmer). Both chat now and then. Like the real client it never waits for the server's answer to a move; it only adopts a correction.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A load test counts every kind of failure by name and goes on; none of them may stop the run.")]
internal sealed class BotClient
{
    private static readonly TimeSpan s_tick = TimeSpan.FromMilliseconds(100);
    private static readonly string[] s_lines = ["xin chao", "hello", "anyone here?", "nice day for parsnips", "ban oi", "trade soon", "gg"];

    private readonly LoadOptions _options;
    private readonly WorldMaps _world;
    private readonly LoadStats _stats;
    private readonly Random _random;
    private readonly Navigator _navigator;
    private readonly object _gate = new();

    private uint _sequence;
    private bool _wasArrived;
    private double _nextFarmAt;
    private double _nextChatAt;

    public BotClient(int index, LoadOptions options, WorldMaps world, LoadStats stats, bool traveller, string target)
    {
        _options = options;
        _world = world;
        _stats = stats;
        _random = new Random(HashCode.Combine(options.Seed, index));
        _navigator = new Navigator(world, new Random(HashCode.Combine(options.Seed, index, 7)), traveller, target);
    }

    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>Runs the player until the token is cancelled.</summary>
    /// <param name="stop">Ends the run.</param>
    /// <returns>A task that completes when the player has disconnected.</returns>
    public async Task RunAsync(CancellationToken stop)
    {
        using WebSocketSession session = new(
            new TransportOptions { Address = _options.Host, Port = _options.Port },
            new WebSocketTransportOptions { Path = _options.Path, UseTls = _options.Tls });
        List<IDisposable> subscriptions = [];
        try
        {
            using CancellationTokenSource connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            await session.ConnectAsync(ct: connectTimeout.Token);
            await session.HandshakeAsync(connectTimeout.Token);

            using StartGuest start = new() { PreferredLanguage = GameLanguage.English };
            AccountResult account = await RequestAsync<AccountResult>(session, start, connectTimeout.Token);
            if (account.Status != AccountStatus.Ok)
            {
                _ = Interlocked.Increment(ref _stats.ConnectFailed);
                _stats.Error("guest:" + account.Status);
                return;
            }

            subscriptions.Add(session.On<MoveCorrection>(this.OnCorrection));
            subscriptions.Add(session.On<MapChanged>(this.OnMapChanged));
            subscriptions.Add(session.On<MapSnapshot>(p => _stats.Snapshot(p.Length)));
            subscriptions.Add(session.On<SessionEnded>(ended =>
            {
                _ = ended;
                _ = Interlocked.Increment(ref _stats.Dropped);
            }));

            using JoinMap join = new() { MapId = Nosum.Gameplay.Maps.GameMaps.DefaultId };
            JoinMapResult joined = await RequestAsync<JoinMapResult>(session, join, connectTimeout.Token);
            if (joined.Status != JoinStatus.Ok || !_world.TryGet(joined.MapId, out MapInfo? map))
            {
                _ = Interlocked.Increment(ref _stats.ConnectFailed);
                _stats.Error("join:" + joined.Status);
                return;
            }

            lock (_gate)
            {
                _navigator.Enter(map, joined.SpawnX, joined.SpawnY, joined.Facing, Now);
                this.NoteArrival();
            }

            _ = Interlocked.Increment(ref _stats.Connected);
            if (_navigator.Traveller)
            {
                _ = Interlocked.Increment(ref _stats.Travellers);
            }

            await this.LoopAsync(session, stop);
        }
        catch (OperationCanceledException)
        {
            // The run ended.
        }
        catch (Exception ex)
        {
            _stats.Error(ex.GetType().Name);
        }
        finally
        {
            foreach (IDisposable subscription in subscriptions)
            {
                subscription.Dispose();
            }

            try
            {
                await session.DisconnectAsync();
            }
            catch (Exception)
            {
                // Already gone.
            }
        }
    }

    private static ValueTask<T> RequestAsync<T>(WebSocketSession session, IPacket request, CancellationToken ct)
        where T : class, IPacket, IPacketStaticOpcode
        => RequestExtensions.RequestAsync<T>(session, request, RequestOptions.Default.WithEncrypt(), ct: ct);

    private async Task LoopAsync(WebSocketSession session, CancellationToken stop)
    {
        using PeriodicTimer timer = new(s_tick);
        double last = Now;
        _nextFarmAt = last + Around(_options.FarmEvery.TotalSeconds);
        _nextChatAt = last + Around(10);
        while (await timer.WaitForNextTickAsync(stop))
        {
            double now = Now;
            double seconds = Math.Min(0.25, now - last);
            last = now;

            using PacketScope<MoveIntent> lease = PacketFactory<MoveIntent>.Acquire();
            MoveIntent intent = lease.Value;
            (byte Slot, int X, int Y) tool = default;
            bool farm = false;
            lock (_gate)
            {
                Position at = _navigator.Step(seconds, now);
                intent.Sequence = ++_sequence;
                intent.X = at.X;
                intent.Y = at.Y;
                intent.Facing = at.Facing;
                intent.Moving = at.Moving;
                if (now >= _nextFarmAt && _navigator.TryNextTool(now, out tool))
                {
                    _nextFarmAt = now + Around(_options.FarmEvery.TotalSeconds);
                    farm = true;
                }
            }

            await session.SendAsync(intent, stop);

            if (farm)
            {
                _ = this.UseToolAsync(session, tool, stop);
            }

            if (now >= _nextChatAt)
            {
                _nextChatAt = now + Around(10);
                _ = this.ChatAsync(session, stop);
            }
        }
    }

    private async Task UseToolAsync(WebSocketSession session, (byte Slot, int X, int Y) tool, CancellationToken stop)
    {
        try
        {
            _ = Interlocked.Increment(ref _stats.FarmSent);
            long started = Stopwatch.GetTimestamp();
            using UseItem request = new() { Slot = tool.Slot, TileX = tool.X, TileY = tool.Y };
            UseItemResult result = await RequestAsync<UseItemResult>(session, request, stop);
            _stats.FarmRoundTrip(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            _stats.Count("farm", result.Status.ToString());
            if (result.Status == FarmStatus.Ok)
            {
                _ = Interlocked.Increment(ref _stats.FarmOk);
            }
        }
        catch (OperationCanceledException)
        {
            // The run ended.
        }
        catch (Exception ex)
        {
            _stats.Error("farm:" + ex.GetType().Name);
        }
    }

    private async Task ChatAsync(WebSocketSession session, CancellationToken stop)
    {
        try
        {
            _ = Interlocked.Increment(ref _stats.ChatSent);
            long started = Stopwatch.GetTimestamp();
            using SendChat request = new() { Channel = ChatChannel.Local, Target = string.Empty, Text = s_lines[_random.Next(s_lines.Length)] };
            ChatResult result = await RequestAsync<ChatResult>(session, request, stop);
            _stats.ChatRoundTrip(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            _stats.Count("chat", result.Status.ToString());
            if (result.Status == ChatStatus.Ok)
            {
                _ = Interlocked.Increment(ref _stats.ChatOk);
            }
        }
        catch (OperationCanceledException)
        {
            // The run ended.
        }
        catch (Exception ex)
        {
            _stats.Error("chat:" + ex.GetType().Name);
        }
    }

    private void OnCorrection(MoveCorrection correction)
    {
        _ = Interlocked.Increment(ref _stats.Corrections);
        _stats.Count("move", correction.Reason.ToString());
        lock (_gate)
        {
            _navigator.Correct(correction.X, correction.Y, correction.Reason == MoveRefusal.Blocked, Now);
        }
    }

    private void OnMapChanged(MapChanged changed)
    {
        _ = Interlocked.Increment(ref _stats.MapChanges);
        lock (_gate)
        {
            if (_world.TryGet(changed.MapId, out MapInfo? map))
            {
                _navigator.Enter(map, changed.X, changed.Y, changed.Facing, Now);
                this.NoteArrival();
            }
        }
    }

    // Counts a traveller once, when it first stands on its target map. Called with the gate held.
    private void NoteArrival()
    {
        if (_navigator.Arrived && !_wasArrived)
        {
            _wasArrived = true;
            _ = Interlocked.Increment(ref _stats.Arrived);
        }
    }

    private double Around(double seconds) => seconds * (0.7 + (_random.NextDouble() * 0.6));
}

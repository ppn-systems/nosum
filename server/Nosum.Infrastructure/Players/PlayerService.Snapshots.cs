// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Player.S2C;

namespace Nosum.Infrastructure.Players;

public sealed partial class PlayerService
{
    private readonly TimeSpan _snapshotInterval;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<string> _mapIds = [];
    private readonly List<PlayerPresence> _players = [];
    private readonly List<PlayerState> _rows = [];
    private int _loopRunning;
    private Task _loop = Task.CompletedTask;

    /// <summary>
    /// Stops the snapshot loop. The loop also ends by itself when the last player leaves, so this
    /// only matters for a prompt shutdown.
    /// </summary>
    /// <returns>A task that completes when the loop has ended.</returns>
    [SuppressMessage("Design", "CA1031", Justification = "Shutdown: the loop only reports its own failure.")]
    public async ValueTask DisposeAsync()
    {
        _presence.PlayerLeft -= this.OnPlayerLeftAsync;
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Cancellation or a send failure while shutting down.
        }

        _stop.Dispose();
    }

    // The loop runs only while players exist: it starts with the first join and ends when the
    // last player has left, so an empty server does no work.
    private void EnsureSnapshotLoop()
    {
        if (Interlocked.CompareExchange(ref _loopRunning, 1, 0) == 0)
        {
            _loop = Task.Run(this.RunSnapshotLoopAsync);
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "One failed tick must not stop snapshots for every map.")]
    private async Task RunSnapshotLoopAsync()
    {
        try
        {
            using PeriodicTimer timer = new(_snapshotInterval, _time);
            while (_presence.PlayerCount > 0 && await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                try
                {
                    await this.PushSnapshotsAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Skip this tick; the next one carries full state again.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
        finally
        {
            Volatile.Write(ref _loopRunning, 0);
            if (_presence.PlayerCount > 0 && !_stop.IsCancellationRequested)
            {
                // Someone joined between the last check and the flag reset.
                this.EnsureSnapshotLoop();
            }
        }
    }

    private async ValueTask PushSnapshotsAsync()
    {
        _mapIds.Clear();
        _presence.CopyActiveMapIds(_mapIds);
        long timeMs = (long)(_time.GetTimestamp() * 1000.0 / _time.TimestampFrequency);

        foreach (string mapId in _mapIds)
        {
            _players.Clear();
            _presence.CopyPlayers(mapId, _players);

            using PacketScope<MapSnapshot> lease = PacketFactory<MapSnapshot>.Acquire();
            MapSnapshot snapshot = lease.Value;
            snapshot.ServerTimeMs = timeMs;
            snapshot.Players.Clear();
            for (int i = 0; i < _players.Count; i++)
            {
                PlayerState row = RowAt(i);
                PlayerPresence player = _players[i];
                lock (player.Gate)
                {
                    row.PlayerId = player.PlayerId;
                    row.X = (float)player.Motion.X;
                    row.Y = (float)player.Motion.Y;
                    row.Facing = (byte)player.Motion.Facing;
                    row.Moving = player.Motion.Moving;
                }

                snapshot.Players.Add(row);
            }

            await _presence.MulticastAsync(mapId, snapshot).ConfigureAwait(false);
        }
    }

    // Rows are reused between ticks: the loop is the only user and each send has completed
    // before the next tick fills them again.
    private PlayerState RowAt(int index)
    {
        while (_rows.Count <= index)
        {
            _rows.Add(PlayerState.Create());
        }

        return _rows[index];
    }
}

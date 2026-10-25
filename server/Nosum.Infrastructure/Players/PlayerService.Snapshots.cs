// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Maps;

namespace Nosum.Infrastructure.Players;

public sealed partial class PlayerService
{
    private readonly TimeSpan _snapshotInterval;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<string> _mapIds = [];
    private readonly List<PlayerPresence> _players = [];
    private readonly List<PlayerState> _rows = [];
    private readonly List<PlayerPresence> _spawns = [];
    private readonly List<uint> _despawns = [];
    private readonly ChunkCoord[] _entered = new ChunkCoord[ChunkInterest.MaxChunks];
    private readonly ChunkCoord[] _left = new ChunkCoord[ChunkInterest.MaxChunks];
    private readonly ChunkCoord[] _interest = new ChunkCoord[ChunkInterest.MaxChunks];
    private long[] _chunkKeys = new long[64];
    private int[] _order = new int[64];
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
                    // Skip this tick; the next one carries the state again.
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

    [SuppressMessage("Design", "CA1031", Justification = "One failed instance must not stop snapshots for the others.")]
    private async ValueTask PushSnapshotsAsync()
    {
        _mapIds.Clear();
        _presence.CopyActiveMapIds(_mapIds);
        long timeMs = (long)(_time.GetTimestamp() * 1000.0 / _time.TimestampFrequency);

        foreach (string mapId in _mapIds)
        {
            try
            {
                await this.PushInstanceAsync(mapId, timeMs).ConfigureAwait(false);
            }
            catch (Exception) when (!_stop.IsCancellationRequested)
            {
                // One instance failing its tick must not starve the others; its next tick starts over.
            }
        }
    }

    // One tick for one instance (docs/adr/ADR-022):
    //  1. read every player's position once and find their chunk;
    //  2. a player whose chunk changed gets their interest set moved (chunk state of the chunks that joined, a notice for the ones that left);
    //  3. sort the players by chunk, so "who stands in chunk c" is a binary search;
    //  4. each player gets a snapshot of the players standing in their interest set, preceded by a despawn for every player who dropped out of sight
    //     and a spawn (look ids, nickname) for every player who came into sight.
    // Nothing here allocates once the scratch arrays have grown to the instance's size.
    private async ValueTask PushInstanceAsync(string instanceKey, long timeMs)
    {
        _players.Clear();
        _presence.CopyPlayers(instanceKey, _players);

        // Only players already in the chunk index take part: a player in the first moments of a join has no interest set yet.
        for (int i = _players.Count - 1; i >= 0; i--)
        {
            if (_players[i].Viewer is null)
            {
                _players.RemoveAt(i);
            }
        }

        int count = _players.Count;
        if (count == 0 || !_maps.TryGet(_players[0].MapId, out MapDefinition? map))
        {
            return;
        }

        this.EnsureScratch(count);
        int tileSize = map.Grid.TileSize;
        for (int i = 0; i < count; i++)
        {
            PlayerPresence player = _players[i];
            PlayerState row = this.RowAt(i);
            double x;
            double y;
            lock (player.Gate)
            {
                x = player.Motion.X;
                y = player.Motion.Y;
                row.PlayerId = player.PlayerId;
                row.X = (float)x;
                row.Y = (float)y;
                row.Facing = (byte)player.Motion.Facing;
                row.Moving = player.Motion.Moving;
            }

            ViewerState viewer = player.Viewer!;
            ChunkCoord chunk = viewer.Interest.Bounds.Clamp(ChunkCoord.OfPixels(x, y, tileSize));
            viewer.Chunk = chunk;
            _chunkKeys[i] = chunk.SortKey;
            _order[i] = i;
        }

        for (int i = 0; i < count; i++)
        {
            await this.UpdateInterestAsync(_players[i]).ConfigureAwait(false);
        }

        Array.Sort(_chunkKeys, _order, 0, count);

        using PacketScope<MapSnapshot> lease = PacketFactory<MapSnapshot>.Acquire();
        MapSnapshot snapshot = lease.Value;
        snapshot.ServerTimeMs = timeMs;
        for (int i = 0; i < count; i++)
        {
            await this.PushToViewerAsync(_players[i], snapshot, count).ConfigureAwait(false);
        }
    }

    // Moves the player's interest set when their chunk changed, then sends what the client is owed: the chunks that left, the state of the chunks that joined.
    [SuppressMessage("Design", "CA1031", Justification = "One dead connection must not cost the rest of the instance its snapshot.")]
    private async ValueTask UpdateInterestAsync(PlayerPresence player)
    {
        ViewerState viewer = player.Viewer!;
        if (viewer.Closed)
        {
            return;
        }

        try
        {
            if (viewer.Chunk != viewer.Interest.Center
                && _chunks.Relocate(player, viewer.Chunk, _entered, out int enteredCount, _left, out int leftCount))
            {
                for (int i = 0; i < leftCount; i++)
                {
                    _ = viewer.PendingEnter.Remove(_left[i]);
                }

                for (int i = 0; i < enteredCount; i++)
                {
                    viewer.PendingEnter.Add(_entered[i]);
                }

                if (leftCount > 0 && !player.Connection.IsDisposed)
                {
                    await _streamer.SendLeftAsync(player, _left.AsMemory(0, leftCount), _stop.Token).ConfigureAwait(false);
                }
            }

            if (viewer.PendingEnter.Count > 0 && !player.Connection.IsDisposed)
            {
                await _streamer.SendPendingStateAsync(player, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception) when (!_stop.IsCancellationRequested)
        {
            // Chunks whose state failed to load stay pending and go out with the next tick.
        }
    }

    // Builds and sends one player's snapshot of their interest set, after the spawns and despawns it implies.
    [SuppressMessage("Design", "CA1031", Justification = "One dead connection must not cost the rest of the instance its snapshot.")]
    private async ValueTask PushToViewerAsync(PlayerPresence viewerPlayer, MapSnapshot snapshot, int count)
    {
        IConnection connection = viewerPlayer.Connection;
        ViewerState viewer = viewerPlayer.Viewer!;
        if (connection.IsDisposed || viewer.Closed)
        {
            return;
        }

        viewer.Stamp++;
        _spawns.Clear();
        _despawns.Clear();
        snapshot.Players.Clear();

        int others = 0;
        int chunkCount = viewer.Interest.CopyTo(_interest);
        for (int c = 0; c < chunkCount; c++)
        {
            long key = _interest[c].SortKey;
            for (int j = LowerBound(key, count); j < count && _chunkKeys[j] == key; j++)
            {
                int index = _order[j];
                snapshot.Players.Add(_rows[index]);
                PlayerPresence other = _players[index];
                if (ReferenceEquals(other, viewerPlayer))
                {
                    continue;
                }

                others++;
                if (!viewer.Shown.TryGetValue(other.PlayerId, out _))
                {
                    _spawns.Add(other);
                }

                viewer.Shown[other.PlayerId] = viewer.Stamp;
            }
        }

        if (viewer.Shown.Count != others)
        {
            foreach (KeyValuePair<uint, int> shown in viewer.Shown)
            {
                if (shown.Value != viewer.Stamp)
                {
                    _despawns.Add(shown.Key);
                }
            }

            for (int i = 0; i < _despawns.Count; i++)
            {
                _ = viewer.Shown.Remove(_despawns[i]);
            }
        }

        try
        {
            for (int i = 0; i < _despawns.Count; i++)
            {
                await _streamer.SendDespawnAsync(viewerPlayer, _despawns[i], _stop.Token).ConfigureAwait(false);
            }

            for (int i = 0; i < _spawns.Count; i++)
            {
                try
                {
                    await _streamer.SendSpawnAsync(viewerPlayer, _spawns[i], _stop.Token).ConfigureAwait(false);
                }
                catch (Exception) when (!_stop.IsCancellationRequested)
                {
                    // The look could not be read: forget the player so the next tick shows them again.
                    _ = viewer.Shown.Remove(_spawns[i].PlayerId);
                }
            }

            await _presence.SendAsync(connection, snapshot, _stop.Token).ConfigureAwait(false);
        }
        catch (Exception) when (!_stop.IsCancellationRequested)
        {
            // A closing connection is cleaned up by its close event.
        }
    }

    // The first index whose chunk key is not below the given one.
    private int LowerBound(long key, int count)
    {
        int low = 0;
        int high = count;
        while (low < high)
        {
            int mid = (int)((uint)(low + high) >> 1);
            if (_chunkKeys[mid] < key)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private void EnsureScratch(int count)
    {
        if (_chunkKeys.Length < count)
        {
            int size = Math.Max(count, _chunkKeys.Length * 2);
            _chunkKeys = new long[size];
            _order = new int[size];
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

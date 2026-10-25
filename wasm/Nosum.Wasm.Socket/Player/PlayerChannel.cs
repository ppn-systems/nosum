// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;

namespace Nosum.Wasm.Socket.Player;

/// <summary>
/// The player's side of the connection: joins a map once the connection is signed in, publishes the
/// local farmer's position to the server about 10 times per second, and hands the server's
/// answers to the frame loop. The frame loop only calls <see cref="Report"/> and the
/// <c>TryTake…</c> methods, which never allocate or await; sending runs on a timer here.
/// </summary>
public sealed class PlayerChannel : IDisposable
{
    /// <summary>Time between position reports while the player moves.</summary>
    public static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(100);

    private readonly IGameSocket _socket;
    private readonly string _mapId;
    private readonly CancellationTokenSource _stop = new();
    private readonly IDisposable[] _subscriptions;
    private readonly Lock _gate = new();
    private uint _playerId;
    private uint _sequence;

    private bool _spawnPending;
    private float _spawnX;
    private float _spawnY;

    private bool _correctionPending;
    private MoveCorrectionInfo _correction;

    private bool _dirty;
    private float _x;
    private float _y;
    private byte _facing;
    private bool _moving;

    /// <summary>Initializes the channel and starts its report timer.</summary>
    /// <param name="socket">The game connection.</param>
    /// <param name="mapId">Map to join.</param>
    public PlayerChannel(IGameSocket socket, string mapId)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentException.ThrowIfNullOrEmpty(mapId);

        _socket = socket;
        _mapId = mapId;
        _subscriptions =
        [
            socket.Subscribe<MoveCorrection>(this.OnCorrection),
        ];
        socket.Ready += this.OnSocketReady;
        _ = Task.Run(() => this.ReportLoopAsync(_stop.Token));
    }

    /// <summary>Raised when the server refuses to put the player on the map. May run on any thread.</summary>
    public event Action<JoinStatus>? JoinRejected;

    /// <summary>Raised after the server put the player on the map, with the map's key. Features that read per-map state (the farm) load it here. May run on any thread.</summary>
    public event Action<string>? Joined;

    /// <summary>Gets the server-assigned id of the local player, or zero while not joined.</summary>
    public uint LocalPlayerId => Volatile.Read(ref _playerId);

    /// <summary>Gets the sequence number of the newest intent sent.</summary>
    public uint LastSentSequence => Volatile.Read(ref _sequence);

    /// <summary>Subscribes to the map snapshots. The packet is pooled: read it in the handler only.</summary>
    /// <param name="handler">Called for every snapshot, on a socket thread.</param>
    /// <returns>A handle that removes the subscription.</returns>
    public IDisposable SubscribeSnapshots(Action<MapSnapshot> handler) => _socket.Subscribe(handler);

    /// <summary>Subscribes to player-left notices. The packet is pooled: read it in the handler only.</summary>
    /// <param name="handler">Called for every notice, on a socket thread.</param>
    /// <returns>A handle that removes the subscription.</returns>
    public IDisposable SubscribePlayerLeft(Action<PlayerLeft> handler) => _socket.Subscribe(handler);

    /// <summary>Publishes the local farmer's current state; the report timer sends it when it changed. Cheap and allocation-free.</summary>
    /// <param name="x">Feet x, world pixels.</param>
    /// <param name="y">Feet y, world pixels.</param>
    /// <param name="facing">Facing, 0 to 3.</param>
    /// <param name="moving">Whether the farmer is walking.</param>
    public void Report(float x, float y, byte facing, bool moving)
    {
        lock (_gate)
        {
            if (_spawnPending)
            {
                return;
            }

            if (_x != x || _y != y || _facing != facing || _moving != moving)
            {
                _x = x;
                _y = y;
                _facing = facing;
                _moving = moving;
                _dirty = true;
            }
        }
    }

    /// <summary>Takes the spawn position of a fresh join, once. The frame loop moves the farmer there.</summary>
    /// <param name="x">Spawn feet x.</param>
    /// <param name="y">Spawn feet y.</param>
    /// <returns><see langword="true"/> when a join completed since the last call.</returns>
    public bool TryTakeSpawn(out float x, out float y)
    {
        lock (_gate)
        {
            x = _spawnX;
            y = _spawnY;
            bool pending = _spawnPending;
            _spawnPending = false;
            return pending;
        }
    }

    /// <summary>Takes the newest correction the server sent, once.</summary>
    /// <param name="correction">The correction.</param>
    /// <returns><see langword="true"/> when a correction arrived since the last call.</returns>
    public bool TryTakeCorrection(out MoveCorrectionInfo correction)
    {
        lock (_gate)
        {
            correction = _correction;
            bool pending = _correctionPending;
            _correctionPending = false;
            return pending;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _socket.Ready -= this.OnSocketReady;
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _stop.Cancel();
        _stop.Dispose();
    }

    private void OnCorrection(MoveCorrection packet)
    {
        lock (_gate)
        {
            _correction = new MoveCorrectionInfo(packet.Sequence, packet.Reason, packet.X, packet.Y);
            _correctionPending = true;
        }
    }

    /// <summary>
    /// Joins the map. Call it whenever the connection is signed in: after the first sign-in and again
    /// after every reconnect and resume. A join needs a signed-in player (the server refuses it otherwise).
    /// </summary>
    public void Join() => _ = this.JoinAsync(_stop.Token);

    // The old player id died with the old connection; the caller signs in again and calls Join.
    private void OnSocketReady() => Volatile.Write(ref _playerId, 0);

    [SuppressMessage("Design", "CA1031", Justification = "A failed join is retried by the next ready event; there is no caller to report to.")]
    private async Task JoinAsync(CancellationToken ct)
    {
        try
        {
            using PacketScope<JoinMap> request = PacketFactory<JoinMap>.Acquire();
            request.Value.MapId = _mapId;
            JoinMapResult result = await _socket.RequestAsync<JoinMapResult>(request.Value, ct).ConfigureAwait(false);

            if (result.Status != JoinStatus.Ok)
            {
                this.JoinRejected?.Invoke(result.Status);
                return;
            }

            lock (_gate)
            {
                _spawnX = result.SpawnX;
                _spawnY = result.SpawnY;
                _spawnPending = true;
                _x = result.SpawnX;
                _y = result.SpawnY;
                _dirty = false;
            }

            Volatile.Write(ref _playerId, result.PlayerId);
            this.Joined?.Invoke(_mapId);
        }
        catch (Exception)
        {
            // Connection dropped or timed out mid-join: the socket reconnects and raises Ready again.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A failed send is dropped; the next report carries fresh state.")]
    private async Task ReportLoopAsync(CancellationToken ct)
    {
        using PeriodicTimer timer = new(ReportInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (this.LocalPlayerId == 0 || _socket.State != GameSocketState.Ready)
                {
                    continue;
                }

                float x;
                float y;
                byte facing;
                bool moving;
                lock (_gate)
                {
                    if (!_dirty)
                    {
                        continue;
                    }

                    _dirty = false;
                    (x, y, facing, moving) = (_x, _y, _facing, _moving);
                }

                try
                {
                    using PacketScope<MoveIntent> lease = PacketFactory<MoveIntent>.Acquire();
                    MoveIntent intent = lease.Value;
                    intent.Sequence = Interlocked.Increment(ref _sequence);
                    intent.X = x;
                    intent.Y = y;
                    intent.Facing = facing;
                    intent.Moving = moving;
                    await _socket.SendAsync(intent, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lock (_gate)
                    {
                        _dirty = true;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
    }
}

/// <summary>A refused move and the position the server keeps.</summary>
/// <param name="Sequence">Sequence number of the refused intent.</param>
/// <param name="Reason">Why it was refused.</param>
/// <param name="X">Feet x the server keeps.</param>
/// <param name="Y">Feet y the server keeps.</param>
public readonly record struct MoveCorrectionInfo(uint Sequence, MoveRefusal Reason, float X, float Y);

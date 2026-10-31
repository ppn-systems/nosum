// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Goals.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Gameplay.Community;

namespace Nosum.Wasm.Socket.Player;

/// <summary>
/// The player's side of the connection: joins a map once the connection is signed in, publishes the
/// local farmer's position to the server about 10 times per second, and hands the server's
/// answers to the frame loop (where to stand after a join or a map change, and move corrections). The frame loop only calls <see cref="Report"/> and the
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

    private bool _entryPending;
    private MapEntry _entry;

    private bool _correctionPending;
    private MoveCorrectionInfo _correction;

    private bool _dirty;
    private float _x;
    private float _y;
    private byte _facing;
    private bool _moving;
    private bool _running;
    private byte _unlocks;

    /// <summary>Initializes the channel and starts its report timer.</summary>
    /// <param name="socket">The game connection.</param>
    /// <param name="mapId">A map the server hosts, named in the join request; the server decides which map the player actually enters.</param>
    public PlayerChannel(IGameSocket socket, string mapId)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentException.ThrowIfNullOrEmpty(mapId);

        _socket = socket;
        _mapId = mapId;
        _subscriptions =
        [
            socket.Subscribe<MoveCorrection>(this.OnCorrection),
            socket.Subscribe<MapChanged>(this.OnMapChanged),
            socket.Subscribe<UnlocksChanged>(p => Volatile.Write(ref _unlocks, p.Unlocks)),
        ];
        socket.Ready += this.OnSocketReady;
        _ = Task.Run(() => this.ReportLoopAsync(_stop.Token));
    }

    /// <summary>
    /// Tells whether the server said a community unlock is set, as far as it changes what the player may walk on (docs/adr/ADR-037 D4). It is learnt with a join or a map
    /// change and when an unlock is set while the player is in Town or on the Beach; the walk grid asks it at every step, so a gated tile opens the moment it is known.
    /// </summary>
    /// <param name="key">An unlock key (<c>CommunityUnlockKeys</c>).</param>
    /// <returns><see langword="true"/> when the unlock is set.</returns>
    public bool IsUnlocked(string key) => CommunityUnlockWire.IsSet(Volatile.Read(ref _unlocks), key);

    /// <summary>Raised when the server refuses to put the player on the map. May run on any thread.</summary>
    public event Action<JoinStatus>? JoinRejected;

    /// <summary>Raised after the server put the player on a map (a join or a map change), with the map's key. Features that read per-map state (the farm) load it here. May run on any thread.</summary>
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
    /// <param name="running">Whether the farmer is running (docs/adr/ADR-081); the server grants the run speed only while it has energy.</param>
    public void Report(float x, float y, byte facing, bool moving, bool running = false)
    {
        lock (_gate)
        {
            if (_entryPending)
            {
                return;
            }

            if (moving || _x != x || _y != y || _facing != facing || _moving != moving || _running != running)
            {
                _x = x;
                _y = y;
                _facing = facing;
                _moving = moving;
                _running = running;
                _dirty = true;
            }
        }
    }

    /// <summary>
    /// Takes the place the server put the player, once: after a join, and after every map change the server made.
    /// The frame loop moves the farmer there and, for another map, fades to it.
    /// </summary>
    /// <param name="entry">The map and the place on it.</param>
    /// <returns><see langword="true"/> when a join or a map change arrived since the last call.</returns>
    public bool TryTakeEntry(out MapEntry entry)
    {
        lock (_gate)
        {
            entry = _entry;
            bool pending = _entryPending;
            _entryPending = false;
            return pending;
        }
    }

    /// <summary>Looks at the place the server put the player without taking it; used to load the right map before the first frame.</summary>
    /// <param name="entry">The map and the place on it.</param>
    /// <returns><see langword="true"/> when an entry is waiting to be taken.</returns>
    public bool TryPeekEntry(out MapEntry entry)
    {
        lock (_gate)
        {
            entry = _entry;
            return _entryPending;
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

    // The server moved the player: it keeps the id, names the new map and the place to stand on.
    private void OnMapChanged(MapChanged packet)
    {
        Volatile.Write(ref _unlocks, packet.Unlocks);
        this.SetEntry(new MapEntry(packet.MapId, packet.X, packet.Y, packet.Facing), packet.PlayerId);
    }

    private void SetEntry(MapEntry entry, uint playerId)
    {
        lock (_gate)
        {
            _entry = entry;
            _entryPending = true;
            _correctionPending = false;
            _correction = default;
            _x = entry.X;
            _y = entry.Y;
            _dirty = false;
        }

        Volatile.Write(ref _playerId, playerId);
        this.Joined?.Invoke(entry.MapId);
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

            Volatile.Write(ref _unlocks, result.Unlocks);
            this.SetEntry(new MapEntry(result.MapId, result.SpawnX, result.SpawnY, result.Facing), result.PlayerId);
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
                bool running;
                lock (_gate)
                {
                    if (!_dirty)
                    {
                        continue;
                    }

                    _dirty = false;
                    (x, y, facing, moving, running) = (_x, _y, _facing, _moving, _running);
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
                    intent.Running = running;
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

/// <summary>Where the server put the player: a map and a place on it.</summary>
/// <param name="MapId">Stable id of the map.</param>
/// <param name="X">Feet x on the map, world pixels.</param>
/// <param name="Y">Feet y on the map, world pixels.</param>
/// <param name="Facing">Facing: 0 down, 1 right, 2 up, 3 left.</param>
public readonly record struct MapEntry(string MapId, float X, float Y, byte Facing);

/// <summary>A refused move and the position the server keeps.</summary>
/// <param name="Sequence">Sequence number of the refused intent.</param>
/// <param name="Reason">Why it was refused.</param>
/// <param name="X">Feet x the server keeps.</param>
/// <param name="Y">Feet y the server keeps.</param>
public readonly record struct MoveCorrectionInfo(uint Sequence, MoveRefusal Reason, float X, float Y);

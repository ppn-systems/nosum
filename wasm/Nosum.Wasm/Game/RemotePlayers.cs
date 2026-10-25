// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics;
using Nosum.Contracts.Player.S2C;
using Nosum.Core.Interpolation;
using Nosum.Wasm.Socket.Player;

namespace Nosum.Wasm.Game;

/// <summary>
/// The other farmers on the map. Socket callbacks write snapshots into a
/// <see cref="SnapshotBuffer"/>; the frame loop reads interpolated poses from it, a little in the
/// past, so movement between 10 Hz snapshots looks smooth (docs/adr/ADR-008).
/// </summary>
internal sealed class RemotePlayers : IDisposable
{
    /// <summary>Most remote farmers drawn at once.</summary>
    public const int MaxPlayers = 63;

    /// <summary>Seconds behind the newest snapshot that remote farmers are drawn: two snapshot intervals.</summary>
    public const double InterpolationDelay = 0.2;

    private readonly PlayerChannel _channel;
    private readonly SnapshotBuffer _buffer = new(MaxPlayers + 1);
    private readonly IDisposable[] _subscriptions;
    private ServerClock _clock;
    private uint _knownLocalId;

    /// <summary>Starts listening to a channel.</summary>
    /// <param name="channel">The player channel.</param>
    public RemotePlayers(PlayerChannel channel)
    {
        _channel = channel;
        _subscriptions =
        [
            channel.SubscribeSnapshots(this.OnSnapshot),
            channel.SubscribePlayerLeft(this.OnPlayerLeft),
        ];
    }

    /// <summary>Gets the number of remote farmers tracked.</summary>
    public int Count => _buffer.Count;

    /// <summary>Returns the local monotonic clock in seconds; the one clock snapshots and frames share.</summary>
    /// <returns>Seconds.</returns>
    public static double LocalNow() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>Packs facing and walking into the buffer's state integer.</summary>
    /// <param name="facing">Facing, 0 to 3.</param>
    /// <param name="moving">Whether walking.</param>
    /// <returns>The state.</returns>
    public static int PackState(byte facing, bool moving) => (facing & 3) | (moving ? 4 : 0);

    /// <summary>Forgets everyone, for example after a reconnect gave this client a new player id.</summary>
    public void Reset()
    {
        _buffer.Clear();
        _knownLocalId = 0;
    }

    /// <summary>Returns the id of the remote farmer at an index below <see cref="Count"/>.</summary>
    /// <param name="index">Index.</param>
    /// <returns>The player id.</returns>
    public uint IdAt(int index) => _buffer.IdAt(index);

    /// <summary>Computes where a remote farmer is drawn at a local time.</summary>
    /// <param name="id">Player id.</param>
    /// <param name="localNow">Local time from <see cref="LocalNow"/>.</param>
    /// <param name="pose">The pose.</param>
    /// <returns><see langword="true"/> when the player is known and the clock is synced.</returns>
    public bool TryGetPose(uint id, double localNow, out EntityPose pose)
    {
        if (!_clock.HasSync)
        {
            pose = default;
            return false;
        }

        return _buffer.TrySample(id, _clock.RemoteNow(localNow) - InterpolationDelay, out pose);
    }

    /// <summary>Returns the time used for walk animation, in seconds, of a render at a local time.</summary>
    /// <param name="localNow">Local time.</param>
    /// <returns>Remote-clock seconds.</returns>
    public double AnimationTime(double localNow) => _clock.RemoteNow(localNow);

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    private void OnSnapshot(MapSnapshot snapshot)
    {
        uint localId = _channel.LocalPlayerId;
        if (localId == 0)
        {
            return;
        }

        if (localId != _knownLocalId)
        {
            _buffer.Clear();
            _knownLocalId = localId;
        }

        double serverTime = snapshot.ServerTimeMs / 1000.0;
        _clock.Observe(serverTime, LocalNow());

        for (int i = 0; i < snapshot.Players.Count; i++)
        {
            PlayerState p = snapshot.Players[i];
            if (p.PlayerId != localId)
            {
                _buffer.Push(p.PlayerId, serverTime, p.X, p.Y, PackState(p.Facing, p.Moving));
            }
        }

        // Snapshots carry everyone on the map, so anyone missing has left.
        for (int i = _buffer.Count - 1; i >= 0; i--)
        {
            uint id = _buffer.IdAt(i);
            if (!Contains(snapshot, id))
            {
                _ = _buffer.Remove(id);
            }
        }
    }

    private void OnPlayerLeft(PlayerLeft left) => _ = _buffer.Remove(left.PlayerId);

    private static bool Contains(MapSnapshot snapshot, uint id)
    {
        for (int i = 0; i < snapshot.Players.Count; i++)
        {
            if (snapshot.Players[i].PlayerId == id)
            {
                return true;
            }
        }

        return false;
    }
}

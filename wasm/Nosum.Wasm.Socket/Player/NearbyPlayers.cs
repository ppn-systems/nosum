// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using Nosum.Contracts.Player.S2C;

namespace Nosum.Wasm.Socket.Player;

/// <summary>
/// The ids of the other players on the local player's map, kept from the map snapshots and leave notices. Trade and co-op invitations
/// pick a player from it. The server decides who is really on the same map; this only fills the picker.
/// </summary>
public sealed class NearbyPlayers : IDisposable
{
    private readonly PlayerChannel _channel;
    private readonly IDisposable[] _subscriptions;
    private readonly Lock _gate = new();
    private readonly HashSet<uint> _ids = [];

    /// <summary>Starts following a player channel.</summary>
    /// <param name="channel">The player channel.</param>
    public NearbyPlayers(PlayerChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        _channel = channel;
        _subscriptions = [channel.SubscribeSnapshots(this.OnSnapshot), channel.SubscribePlayerLeft(this.OnLeft)];
    }

    /// <summary>Raised when someone appeared or left. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Returns the ids now on the map, ascending.</summary>
    /// <returns>A copy.</returns>
    public uint[] Ids()
    {
        lock (_gate)
        {
            uint[] copy = [.. _ids];
            Array.Sort(copy);
            return copy;
        }
    }

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
        uint local = _channel.LocalPlayerId;
        bool changed = false;
        lock (_gate)
        {
            HashSet<uint> now = [];
            foreach (PlayerState player in snapshot.Players)
            {
                if (player.PlayerId != local)
                {
                    _ = now.Add(player.PlayerId);
                }
            }

            if (!_ids.SetEquals(now))
            {
                _ids.Clear();
                _ids.UnionWith(now);
                changed = true;
            }
        }

        if (changed)
        {
            this.Changed?.Invoke();
        }
    }

    private void OnLeft(PlayerLeft left)
    {
        bool removed;
        lock (_gate)
        {
            removed = _ids.Remove(left.PlayerId);
        }

        if (removed)
        {
            this.Changed?.Invoke();
        }
    }
}

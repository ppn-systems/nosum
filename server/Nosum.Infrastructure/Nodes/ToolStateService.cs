// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Node.S2C;
using Nosum.Gameplay.Nodes;

namespace Nosum.Infrastructure.Nodes;

/// <summary>
/// <see cref="IToolState"/> in memory: one small record per player who used a tool. The swing cooldown uses the injected clock, so tests that fake time
/// control it; the can's water is pushed to the player after every change.
/// </summary>
public sealed class ToolStateService : IToolState
{
    private readonly ConcurrentDictionary<Guid, Entry> _players = new();
    private readonly TimeProvider _time;
    private readonly IPlayerNotifier? _notifier;

    /// <summary>Initializes the service.</summary>
    /// <param name="time">The clock the cooldown runs on.</param>
    /// <param name="notifier">Pushes the can's state to the player; <see langword="null"/> in tests that do not listen.</param>
    public ToolStateService(TimeProvider time, IPlayerNotifier? notifier = null)
    {
        ArgumentNullException.ThrowIfNull(time);

        _time = time;
        _notifier = notifier;
    }

    /// <inheritdoc/>
    public bool TryBeginSwing(Guid playerId)
    {
        long now = _time.GetUtcNow().ToUnixTimeMilliseconds();
        Entry entry = _players.GetOrAdd(playerId, static _ => new Entry());
        lock (entry)
        {
            if (entry.LastSwingMs != long.MinValue && now - entry.LastSwingMs < NodeLimits.SwingCooldownMs)
            {
                return false;
            }

            entry.LastSwingMs = now;
            return true;
        }
    }

    /// <inheritdoc/>
    public int WaterLeft(Guid playerId)
    {
        if (!_players.TryGetValue(playerId, out Entry? entry))
        {
            return NodeLimits.WateringCanCapacity;
        }

        lock (entry)
        {
            return entry.Water;
        }
    }

    /// <inheritdoc/>
    public int CapacityOf(Guid playerId)
    {
        if (!_players.TryGetValue(playerId, out Entry? entry))
        {
            return NodeLimits.WateringCanCapacity;
        }

        lock (entry)
        {
            return entry.Capacity;
        }
    }

    /// <inheritdoc/>
    public async ValueTask<int> UseWaterAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        Entry entry = _players.GetOrAdd(playerId, static _ => new Entry());
        int left;
        int capacity;
        lock (entry)
        {
            if (entry.Water <= 0)
            {
                return -1;
            }

            left = --entry.Water;
            capacity = entry.Capacity;
        }

        await this.PushAsync(playerId, left, capacity, cancellationToken).ConfigureAwait(false);
        return left;
    }

    /// <inheritdoc/>
    public ValueTask<int> RefillAsync(Guid playerId, CancellationToken cancellationToken = default)
        => this.RefillAsync(playerId, NodeLimits.WateringCanCapacity, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<int> RefillAsync(Guid playerId, int capacity, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, byte.MaxValue);

        Entry entry = _players.GetOrAdd(playerId, static _ => new Entry());
        lock (entry)
        {
            entry.Capacity = capacity;
            entry.Water = capacity;
        }

        await this.PushAsync(playerId, capacity, capacity, cancellationToken).ConfigureAwait(false);
        return capacity;
    }

    [SuppressMessage("Design", "CA1031", Justification = "The change is made; a failed push only delays the display until the next one.")]
    private async ValueTask PushAsync(Guid playerId, int left, int capacity, CancellationToken cancellationToken)
    {
        if (_notifier is null)
        {
            return;
        }

        try
        {
            using PacketScope<ToolState> lease = PacketFactory<ToolState>.Acquire();
            lease.Value.WaterLeft = (byte)left;
            lease.Value.WaterCapacity = (byte)capacity;
            _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best effort, see above.
        }
    }

    private sealed class Entry
    {
        public long LastSwingMs = long.MinValue;

        public int Water = NodeLimits.WateringCanCapacity;

        public int Capacity = NodeLimits.WateringCanCapacity;
    }
}

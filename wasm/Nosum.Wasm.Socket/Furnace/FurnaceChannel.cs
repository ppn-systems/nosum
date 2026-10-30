// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;

namespace Nosum.Wasm.Socket.Furnace;

/// <summary>One furnace with a job as the client mirrors it.</summary>
/// <param name="ObjectId">The furnace's id.</param>
/// <param name="OreItemId">The ore being smelted.</param>
/// <param name="ReadyAt">When the bar is ready on the client's own clock (arrival time plus the server's remaining time).</param>
/// <param name="Done">Whether the server said the bar was already ready.</param>
public readonly record struct FurnaceJobInfo(int ObjectId, int OreItemId, DateTimeOffset ReadyAt, bool Done);

/// <summary>
/// The client's mirror of the furnaces of the player's place (docs/adr/ADR-024 D4, D7). The server sends the remaining time with every state, the channel counts it down
/// on its own clock, so nothing ticks over the wire. Loaded after every join; kept current by <see cref="FurnaceChanged"/> pushes.
/// </summary>
public sealed class FurnaceChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly TimeProvider _time;
    private readonly IDisposable _push;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, FurnaceJobInfo> _jobs = [];
    private int _version;

    /// <summary>Initializes the channel and starts listening for pushes.</summary>
    /// <param name="socket">The game connection.</param>
    /// <param name="time">The client clock the countdown runs on; <see langword="null"/> for the system clock.</param>
    public FurnaceChannel(IGameSocket socket, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
        _time = time ?? TimeProvider.System;
        _push = socket.Subscribe<FurnaceChanged>(this.OnChanged);
    }

    /// <summary>Raised when a furnace changed state or the mirror was loaded. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised with the server's answer to a start or collect request. May run on any thread.</summary>
    public event Action<FurnaceStatus>? Completed;

    /// <summary>Raised when a running batch reaches its ready time on this client's clock, with the furnace's object id. May run on any thread.</summary>
    public event Action<int>? Finished;

    /// <summary>Gets a number that changes whenever the mirror does.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>Gets the state of a furnace now.</summary>
    /// <param name="objectId">The furnace's id.</param>
    /// <param name="oreItemId">The ore being smelted; zero when idle.</param>
    /// <param name="remainingMs">Milliseconds until ready; zero when ready or idle.</param>
    /// <returns>Idle, working or ready.</returns>
    public FurnaceStateCode StateOf(int objectId, out int oreItemId, out int remainingMs)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(objectId, out FurnaceJobInfo job))
            {
                oreItemId = 0;
                remainingMs = 0;
                return FurnaceStateCode.Idle;
            }

            oreItemId = job.OreItemId;
            long left = job.Done ? 0 : (long)(job.ReadyAt - _time.GetUtcNow()).TotalMilliseconds;
            remainingMs = (int)Math.Max(0, Math.Min(int.MaxValue, left));
            return remainingMs == 0 ? FurnaceStateCode.Ready : FurnaceStateCode.Working;
        }
    }

    /// <summary>Tells whether a furnace is smelting right now (for the lit sprite).</summary>
    /// <param name="objectId">The furnace's id.</param>
    /// <returns><see langword="true"/> while the bar is not ready.</returns>
    public bool IsWorking(int objectId) => this.StateOf(objectId, out _, out _) == FurnaceStateCode.Working;

    /// <summary>Reads the furnaces of the place. Call after joining a map.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <summary>Asks the server to start a job.</summary>
    /// <param name="objectId">The furnace's id.</param>
    /// <param name="oreItemId">The ore to smelt.</param>
    public void Start(int objectId, int oreItemId) => _ = this.StartAsync(objectId, oreItemId);

    /// <summary>Asks the server to hand over the finished bar.</summary>
    /// <param name="objectId">The furnace's id.</param>
    public void Collect(int objectId) => _ = this.CollectAsync(objectId);

    /// <inheritdoc/>
    public void Dispose() => _push.Dispose();

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried at the next join; there is no caller to report to.")]
    private async Task LoadAsync()
    {
        try
        {
            using PacketScope<GetFurnaces> request = PacketFactory<GetFurnaces>.Acquire();
            FurnaceSnapshot snapshot = await _socket.RequestAsync<FurnaceSnapshot>(request.Value).ConfigureAwait(false);
            DateTimeOffset now = _time.GetUtcNow();
            lock (_gate)
            {
                _jobs.Clear();
                foreach (FurnaceRow row in snapshot.Furnaces)
                {
                    this.Store(row.ObjectId, row.State, row.OreItemId, row.RemainingMs, now);
                }

                _ = Interlocked.Increment(ref _version);
            }

            this.Changed?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task StartAsync(int objectId, int oreItemId)
    {
        try
        {
            using PacketScope<StartSmelting> request = PacketFactory<StartSmelting>.Acquire();
            request.Value.ObjectId = objectId;
            request.Value.OreItemId = oreItemId;
            FurnaceResult result = await _socket.RequestAsync<FurnaceResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(result.Status);
        }
        catch (Exception)
        {
            // Connection dropped; nothing started.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task CollectAsync(int objectId)
    {
        try
        {
            using PacketScope<CollectFurnace> request = PacketFactory<CollectFurnace>.Acquire();
            request.Value.ObjectId = objectId;
            FurnaceResult result = await _socket.RequestAsync<FurnaceResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(result.Status);
        }
        catch (Exception)
        {
            // Connection dropped; nothing collected.
        }
    }

    private void OnChanged(FurnaceChanged packet)
    {
        DateTimeOffset now = _time.GetUtcNow();
        lock (_gate)
        {
            this.Store(packet.ObjectId, packet.State, packet.OreItemId, packet.RemainingMs, now);
            _ = Interlocked.Increment(ref _version);
        }

        this.Changed?.Invoke();
    }

    // Call under the lock.
    private void Store(int objectId, FurnaceStateCode state, int oreItemId, int remainingMs, DateTimeOffset now)
    {
        if (state == FurnaceStateCode.Idle)
        {
            _ = _jobs.Remove(objectId);
            return;
        }

        DateTimeOffset readyAt = now.AddMilliseconds(remainingMs);
        _jobs[objectId] = new FurnaceJobInfo(objectId, oreItemId, readyAt, state == FurnaceStateCode.Ready);
        if (state != FurnaceStateCode.Ready)
        {
            _ = this.FinishAsync(objectId, readyAt);
        }
    }

    // Waits for the batch's ready time on the client's clock, then raises Finished unless the batch was replaced or collected meanwhile.
    [SuppressMessage("Design", "CA1031", Justification = "A timer that cannot run changes nothing; the bar shows its state on the next read.")]
    private async Task FinishAsync(int objectId, DateTimeOffset readyAt)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, (readyAt - _time.GetUtcNow()).TotalMilliseconds)), _time).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        lock (_gate)
        {
            if (!_jobs.TryGetValue(objectId, out FurnaceJobInfo job) || job.ReadyAt != readyAt)
            {
                return;
            }
        }

        this.Finished?.Invoke(objectId);
    }
}

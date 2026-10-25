// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.GroundItem;
using Nosum.Contracts.GroundItem.C2S;
using Nosum.Contracts.GroundItem.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Gameplay.Maps;

namespace Nosum.Wasm.Socket.GroundItem;

/// <summary>One item lying on the ground as the client mirrors it.</summary>
/// <param name="Id">Ground item id.</param>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">How many lie there.</param>
/// <param name="X">X on the map, pixels.</param>
/// <param name="Y">Y on the map, pixels.</param>
/// <param name="SeenAtMs">When the client learned of it (<see cref="Environment.TickCount64"/>), for the pop-in animation.</param>
public readonly record struct GroundItemInfo(long Id, int ItemId, int Quantity, int X, int Y, long SeenAtMs);

/// <summary>An item flying to the player who picked it up. An empty slot has <see cref="ItemId"/> zero.</summary>
/// <param name="ItemId">The item, or zero for an empty slot.</param>
/// <param name="FromX">Start x, pixels.</param>
/// <param name="FromY">Start y, pixels.</param>
/// <param name="ToX">End x, pixels.</param>
/// <param name="ToY">End y, pixels.</param>
/// <param name="StartedAtMs">When the flight began (<see cref="Environment.TickCount64"/>).</param>
public readonly record struct FlyingItem(int ItemId, int FromX, int FromY, int ToX, int ToY, long StartedAtMs);

/// <summary>
/// The client's mirror of the items lying on the ground of the player's current place (docs/adr/ADR-021 D5-D6). The server owns them and
/// picks them up by itself when the player walks near, so there is no action to send; the mirror only follows the pushes. The frame loop
/// reads it with <see cref="Read"/>, <see cref="Count"/> and <see cref="At"/> and never allocates or awaits. The server sends the items of a chunk once, when the chunk joins the
/// player's interest set (<see cref="ChunkState"/>), and then only changes in it; when the chunk leaves the set (<see cref="ChunkLeft"/>) its items are dropped
/// (docs/adr/ADR-022 D6). An item belongs to the chunk its pixel position falls in. A pickup leaves a short-lived
/// <see cref="FlyingItem"/> the scene draws moving to the taker.
/// </summary>
public sealed class GroundItemChannel : IDisposable
{
    /// <summary>The number of flights that can be on screen at once; older ones are overwritten.</summary>
    public const int FlyCapacity = 16;

    // Every map is drawn on a 16 px grid (assets-pipeline), which is what the server buckets ground items by.
    private const int TileSize = 16;

    private readonly IGameSocket _socket;
    private readonly IDisposable[] _subscriptions;
    private readonly Lock _gate = new();
    private readonly List<GroundItemInfo> _items = [];
    private readonly FlyingItem[] _flights = new FlyingItem[FlyCapacity];
    private string _mapId = string.Empty;
    private int _nextFlight;
    private int _version;

    /// <summary>Initializes the channel and starts listening for pushed changes.</summary>
    /// <param name="socket">The game connection.</param>
    public GroundItemChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
        _subscriptions = [socket.Subscribe<GroundItemSpawned>(this.OnSpawned), socket.Subscribe<GroundItemTaken>(this.OnTaken), socket.Subscribe<ChunkState>(this.OnChunkState), socket.Subscribe<ChunkLeft>(this.OnChunkLeft)];
    }

    /// <summary>Raised when the mirror was loaded or changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Gets the key of the map the mirrored items lie on; empty before the first load.</summary>
    public string MapId
    {
        get
        {
            lock (_gate)
            {
                return _mapId;
            }
        }
    }

    /// <summary>Gets a number that changes whenever the mirror does.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>Gets the number of mirrored items. Use under <see cref="Read"/>.</summary>
    public int Count => _items.Count;

    /// <summary>Takes the lock that keeps the mirror steady while the frame loop reads it; dispose to release.</summary>
    /// <returns>The held lock.</returns>
    public Lock.Scope Read() => _gate.EnterScope();

    /// <summary>Reads an item by position in the list; call under <see cref="Read"/>.</summary>
    /// <param name="index">Zero-based position, below <see cref="Count"/>.</param>
    /// <returns>The item.</returns>
    public GroundItemInfo At(int index) => _items[index];

    /// <summary>Reads a flight slot; call under <see cref="Read"/>.</summary>
    /// <param name="index">Zero-based slot, below <see cref="FlyCapacity"/>.</param>
    /// <returns>The flight; <see cref="FlyingItem.ItemId"/> is zero for an empty slot.</returns>
    public FlyingItem FlightAt(int index) => _flights[index];

    /// <summary>Loads the items of the place the player is in. Call after joining a map.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried when the next join completes; there is no caller to report to.")]
    private async Task LoadAsync()
    {
        try
        {
            using PacketScope<GetGroundItems> request = PacketFactory<GetGroundItems>.Acquire();
            GroundItemsSnapshot snapshot = await _socket.RequestAsync<GroundItemsSnapshot>(request.Value).ConfigureAwait(false);
            long now = Environment.TickCount64;
            lock (_gate)
            {
                _items.Clear();
                Array.Clear(_flights);
                _mapId = snapshot.MapId;
                foreach (GroundItemRow row in snapshot.Items)
                {
                    // Items that were already there when the player arrived do not pop in.
                    _items.Add(new GroundItemInfo(row.Id, row.ItemId, row.Quantity, row.X, row.Y, now - 1_000));
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

    private void OnChunkState(ChunkState packet)
    {
        lock (_gate)
        {
            // A chunk of another map (a late push after a map change, or one that beat the load of this map) is not ours; the load covers it.
            if (!string.Equals(packet.MapId, _mapId, StringComparison.Ordinal))
            {
                return;
            }

            ChunkCoord chunk = new(packet.ChunkX, packet.ChunkY);
            long now = Environment.TickCount64;
            _ = this.RemoveChunk(chunk);
            foreach (GroundItemRow row in packet.GroundItems)
            {
                // Items that were already lying there when the chunk came into sight do not pop in.
                _items.Add(new GroundItemInfo(row.Id, row.ItemId, row.Quantity, row.X, row.Y, now - 1_000));
            }

            _ = Interlocked.Increment(ref _version);
        }

        this.Changed?.Invoke();
    }

    private void OnChunkLeft(ChunkLeft packet)
    {
        bool removed;
        lock (_gate)
        {
            removed = string.Equals(packet.MapId, _mapId, StringComparison.Ordinal) && this.RemoveChunk(new ChunkCoord(packet.ChunkX, packet.ChunkY));
            if (removed)
            {
                _ = Interlocked.Increment(ref _version);
            }
        }

        if (removed)
        {
            this.Changed?.Invoke();
        }
    }

    // Drops the items of one chunk. Call under the lock.
    private bool RemoveChunk(ChunkCoord chunk) => _items.RemoveAll(i => ChunkCoord.OfPixels(i.X, i.Y, TileSize) == chunk) > 0;

    private void OnSpawned(GroundItemSpawned packet)
    {
        lock (_gate)
        {
            // A push for another map (a late one after a map change) is not ours.
            if (!string.Equals(packet.MapId, _mapId, StringComparison.Ordinal))
            {
                return;
            }

            GroundItemRow row = packet.Item;
            GroundItemInfo item = new(row.Id, row.ItemId, row.Quantity, row.X, row.Y, Environment.TickCount64);
            int at = _items.FindIndex(i => i.Id == row.Id);
            if (at >= 0)
            {
                _items[at] = item;
            }
            else
            {
                _items.Add(item);
            }

            _ = Interlocked.Increment(ref _version);
        }

        this.Changed?.Invoke();
    }

    private void OnTaken(GroundItemTaken packet)
    {
        lock (_gate)
        {
            if (!string.Equals(packet.MapId, _mapId, StringComparison.Ordinal))
            {
                return;
            }

            int at = _items.FindIndex(i => i.Id == packet.Id);
            if (at < 0)
            {
                return;
            }

            GroundItemInfo gone = _items[at];
            _items.RemoveAt(at);
            if (packet.Reason == GroundItemGone.PickedUp)
            {
                _flights[_nextFlight] = new FlyingItem(gone.ItemId, gone.X, gone.Y, packet.ToX, packet.ToY, Environment.TickCount64);
                _nextFlight = (_nextFlight + 1) % FlyCapacity;
            }

            _ = Interlocked.Increment(ref _version);
        }

        this.Changed?.Invoke();
    }
}

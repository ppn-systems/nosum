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
using Nosum.Contracts.World.S2C;
using Nosum.Gameplay.Maps;
using Nosum.Wasm.Socket.Chunks;

namespace Nosum.Wasm.Socket.Placeable;

/// <summary>One placed object as the client mirrors it.</summary>
/// <param name="Id">Object id.</param>
/// <param name="ItemId">The inventory item it is made from.</param>
/// <param name="TileX">Left tile of the footprint.</param>
/// <param name="TileY">Top tile of the footprint.</param>
/// <param name="Rotation">Rotation, zero to three.</param>
/// <param name="PlacedAtMs">When it was placed, Unix milliseconds: a sprinkler waters the crops beside it from the day after.</param>
public readonly record struct PlacedObjectInfo(int Id, int ItemId, int TileX, int TileY, int Rotation, long PlacedAtMs);

/// <summary>
/// The client's mirror of the objects placed in the player's current place, plus the three actions that change them. The server sends the objects of a chunk once, when the chunk
/// joins the player's interest set (<see cref="ChunkState"/>), and then only changes in it; when the chunk leaves the set (<see cref="ChunkLeft"/>) its objects are dropped
/// (docs/adr/ADR-022 D6). An object is sent with every chunk its footprint overlaps, and the server sends it again when one of those chunks leaves while another it
/// overlaps stays in the set. The server owns the
/// objects: an action shows only once its <see cref="PlacedChanged"/> push arrives. The frame loop reads the mirror with <see cref="Read"/>
/// and <see cref="Count"/> / <see cref="At"/> and never allocates or awaits; <see cref="Version"/> says when it changed, so the scene can rebuild its collision grid then.
/// </summary>
public sealed class PlaceableChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable[] _subscriptions;
    private readonly Lock _gate = new();
    private readonly List<PlacedObjectInfo> _items = [];
    private string _mapId = string.Empty;
    private int _version;
    private readonly ChunkLoadGate _loads = new();

    /// <summary>Initializes the channel and starts listening for pushed changes.</summary>
    /// <param name="socket">The game connection.</param>
    public PlaceableChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
        _subscriptions = [socket.Subscribe<PlacedChanged>(this.OnChanged), socket.Subscribe<ChunkState>(this.OnChunkState), socket.Subscribe<ChunkLeft>(this.OnChunkLeft)];
    }

    /// <summary>Raised when the server answered a place, move or pick-up request. May run on any thread.</summary>
    public event Action<PlaceStatus>? Completed;

    /// <summary>Raised when the server answered a pick-up request, so the game can confirm a piece went into the bag. May run on any thread.</summary>
    public event Action<PlaceStatus>? PickUpCompleted;

    /// <summary>Raised when the mirror was loaded or changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Gets the key of the map the mirrored objects stand on; empty before the first load or outside a placeable map.</summary>
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

    /// <summary>Takes the lock that keeps the mirror steady while the frame loop reads it; dispose to release.</summary>
    /// <returns>The held lock.</returns>
    public Lock.Scope Read() => _gate.EnterScope();

    /// <summary>Gets the number of mirrored objects. Use under <see cref="Read"/>.</summary>
    public int Count => _items.Count;

    /// <summary>Reads an object by position in the list; call under <see cref="Read"/>.</summary>
    /// <param name="index">Zero-based position, below <see cref="Count"/>.</param>
    /// <returns>The object.</returns>
    public PlacedObjectInfo At(int index) => _items[index];

    /// <summary>Reads the objects of the chunks the player can see and replaces the mirror with them. Call after joining a map: it also names the map the mirror belongs to. Later chunks arrive as pushes.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <summary>Asks the server to place the item in an inventory slot.</summary>
    /// <param name="slot">Inventory slot.</param>
    /// <param name="tileX">Left tile of the footprint.</param>
    /// <param name="tileY">Top tile of the footprint.</param>
    /// <param name="rotation">Rotation.</param>
    public void Place(int slot, int tileX, int tileY, int rotation) => _ = this.PlaceAsync(slot, tileX, tileY, rotation);

    /// <summary>Asks the server to move or rotate an object.</summary>
    /// <param name="objectId">Object id.</param>
    /// <param name="tileX">New left tile.</param>
    /// <param name="tileY">New top tile.</param>
    /// <param name="rotation">New rotation.</param>
    public void Move(int objectId, int tileX, int tileY, int rotation) => _ = this.MoveAsync(objectId, tileX, tileY, rotation);

    /// <summary>Asks the server to pick an object up into the inventory.</summary>
    /// <param name="objectId">Object id.</param>
    public void PickUp(int objectId) => _ = this.PickUpAsync(objectId);

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
        int gen = _loads.BeginLoad();
        try
        {
            using PacketScope<GetPlaced> request = PacketFactory<GetPlaced>.Acquire();
            PlacedSnapshot snapshot = await _socket.RequestAsync<PlacedSnapshot>(request.Value).ConfigureAwait(false);
            if (!_loads.IsCurrent(gen))
            {
                return;
            }

            lock (_gate)
            {
                _items.Clear();
                _mapId = snapshot.MapId;
                foreach (PlacedObjectRow row in snapshot.Objects)
                {
                    _items.Add(new PlacedObjectInfo(row.Id, row.ItemId, row.TileX, row.TileY, row.Rotation, row.PlacedAtMs));
                }

                _ = Interlocked.Increment(ref _version);
            }

            this.Changed?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
        finally
        {
            _loads.EndLoad();
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost action changes nothing; the player tries again.")]
    private async Task PlaceAsync(int slot, int tileX, int tileY, int rotation)
    {
        try
        {
            using PacketScope<PlaceObject> request = PacketFactory<PlaceObject>.Acquire();
            request.Value.Slot = (byte)slot;
            request.Value.TileX = tileX;
            request.Value.TileY = tileY;
            request.Value.Rotation = (byte)rotation;
            PlaceResult result = await _socket.RequestAsync<PlaceResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(result.Status);
        }
        catch (Exception)
        {
            // Connection dropped; nothing was applied.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost action changes nothing; the player tries again.")]
    private async Task MoveAsync(int objectId, int tileX, int tileY, int rotation)
    {
        try
        {
            using PacketScope<MoveObject> request = PacketFactory<MoveObject>.Acquire();
            request.Value.ObjectId = objectId;
            request.Value.TileX = tileX;
            request.Value.TileY = tileY;
            request.Value.Rotation = (byte)rotation;
            PlaceResult result = await _socket.RequestAsync<PlaceResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(result.Status);
        }
        catch (Exception)
        {
            // Connection dropped; nothing was applied.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost action changes nothing; the player tries again.")]
    private async Task PickUpAsync(int objectId)
    {
        try
        {
            using PacketScope<PickUpObject> request = PacketFactory<PickUpObject>.Acquire();
            request.Value.ObjectId = objectId;
            PlaceResult result = await _socket.RequestAsync<PlaceResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(result.Status);
            this.PickUpCompleted?.Invoke(result.Status);
        }
        catch (Exception)
        {
            // Connection dropped; nothing was applied.
        }
    }

    private void OnChunkState(ChunkState packet)
    {
        lock (_gate)
        {
            if (!_loads.TryAcceptChunkMap(ref _mapId, packet.MapId, _items.Clear))
            {
                return;
            }

            ChunkCoord chunk = new(packet.ChunkX, packet.ChunkY);
            _ = _items.RemoveAll(o => ChunkCoord.OfTile(o.TileX, o.TileY) == chunk);
            foreach (PlacedObjectRow row in packet.Placed)
            {
                // A piece that crosses into this chunk from a neighbour is already mirrored: replace it, never list it twice.
                _ = _items.RemoveAll(o => o.Id == row.Id);
                _items.Add(new PlacedObjectInfo(row.Id, row.ItemId, row.TileX, row.TileY, row.Rotation, row.PlacedAtMs));
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
            ChunkCoord chunk = new(packet.ChunkX, packet.ChunkY);
            removed = string.Equals(packet.MapId, _mapId, StringComparison.Ordinal) && _items.RemoveAll(o => ChunkCoord.OfTile(o.TileX, o.TileY) == chunk) > 0;
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

    private void OnChanged(PlacedChanged packet)
    {
        lock (_gate)
        {
            // A push for another map (a late one after a map change) is not ours.
            if (!string.Equals(packet.MapId, _mapId, StringComparison.Ordinal))
            {
                return;
            }

            foreach (int id in packet.Removed)
            {
                _ = _items.RemoveAll(o => o.Id == id);
            }

            foreach (PlacedObjectRow row in packet.Upserted)
            {
                PlacedObjectInfo item = new(row.Id, row.ItemId, row.TileX, row.TileY, row.Rotation, row.PlacedAtMs);
                int at = _items.FindIndex(o => o.Id == row.Id);
                if (at >= 0)
                {
                    _items[at] = item;
                }
                else
                {
                    _items.Add(item);
                }
            }

            _ = Interlocked.Increment(ref _version);
        }

        this.Changed?.Invoke();
    }
}

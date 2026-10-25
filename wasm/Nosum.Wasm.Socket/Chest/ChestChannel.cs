// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Wasm.Socket.Inventory;

namespace Nosum.Wasm.Socket.Chest;

/// <summary>
/// The client's mirror of the one chest the player has open (docs/adr/ADR-024 D4, D7). The server sends the contents when the chest is opened and then pushes the whole
/// slot list after every change; a move only shows once that push arrives. Closed by the server (<see cref="ChestClosed"/>, a refused move out of reach) or by the player.
/// </summary>
public sealed class ChestChannel : IDisposable
{
    /// <summary>The slot number that asks the server to pick the first slot that fits.</summary>
    public const int AnySlot = 255;

    // Same as ChestRules.Capacity in Nosum.Gameplay (which this project does not reference); a test pins them together.
    private const int SlotCount = 36;

    private readonly IGameSocket _socket;
    private readonly IDisposable[] _subscriptions;
    private readonly Lock _gate = new();
    private readonly SlotInfo[] _slots = new SlotInfo[SlotCount];
    private int _objectId;

    /// <summary>Initializes the channel and starts listening for pushes.</summary>
    /// <param name="socket">The game connection.</param>
    public ChestChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
        _subscriptions = [socket.Subscribe<ChestUpdate>(this.OnUpdate), socket.Subscribe<ChestClosed>(this.OnClosed)];
    }

    /// <summary>Raised when the chest opened or its slots changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised when the chest is no longer open (closed by the server or the player). May run on any thread.</summary>
    public event Action? Closed;

    /// <summary>Raised with the server's answer to an open or a move request. May run on any thread.</summary>
    public event Action<ChestStatus>? Completed;

    /// <summary>Gets the number of slots of a chest.</summary>
    public static int Capacity => SlotCount;

    /// <summary>Gets the id of the open chest; zero when none is open.</summary>
    public int ObjectId
    {
        get
        {
            lock (_gate)
            {
                return _objectId;
            }
        }
    }

    /// <summary>Gets whether a chest is open.</summary>
    public bool IsOpen => this.ObjectId != 0;

    /// <summary>Reads a slot of the open chest.</summary>
    /// <param name="slot">Slot number.</param>
    /// <returns>The slot's content; empty outside the chest.</returns>
    public SlotInfo SlotAt(int slot)
    {
        lock (_gate)
        {
            return (uint)slot < (uint)_slots.Length ? _slots[slot] : default;
        }
    }

    /// <summary>Asks the server to open a chest; it opens only if the server says Ok.</summary>
    /// <param name="objectId">Id of the placed chest.</param>
    public void Open(int objectId) => _ = this.OpenAsync(objectId);

    /// <summary>Closes the open chest and tells the server.</summary>
    public void Close()
    {
        int id;
        lock (_gate)
        {
            id = _objectId;
            _objectId = 0;
            Array.Clear(_slots);
        }

        if (id != 0)
        {
            _ = this.SendCloseAsync(id);
            this.Closed?.Invoke();
        }
    }

    /// <summary>Asks the server to move a stack, or part of it.</summary>
    /// <param name="fromSide">Where the stack is.</param>
    /// <param name="fromSlot">Its slot.</param>
    /// <param name="toSide">Where it goes.</param>
    /// <param name="toSlot">The slot it goes to, or <see cref="AnySlot"/> for the first that fits.</param>
    /// <param name="quantity">How many; zero moves the whole stack.</param>
    public void Move(ChestSide fromSide, int fromSlot, ChestSide toSide, int toSlot, int quantity = 0)
    {
        int id = this.ObjectId;
        if (id != 0)
        {
            _ = this.MoveAsync(id, fromSide, fromSlot, toSide, toSlot, quantity);
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

    // Call under the lock.
    private void Apply(List<InventorySlotRow> rows)
    {
        foreach (InventorySlotRow row in rows)
        {
            if (row.Slot < _slots.Length)
            {
                _slots[row.Slot] = new SlotInfo(row.ItemId, row.Quantity);
            }
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task OpenAsync(int objectId)
    {
        try
        {
            using PacketScope<OpenChest> request = PacketFactory<OpenChest>.Acquire();
            request.Value.ObjectId = objectId;
            ChestSnapshot snapshot = await _socket.RequestAsync<ChestSnapshot>(request.Value).ConfigureAwait(false);
            if (snapshot.Status == ChestStatus.Ok)
            {
                lock (_gate)
                {
                    _objectId = snapshot.ObjectId;
                    Array.Clear(_slots);
                    this.Apply(snapshot.Slots);
                }

                this.Changed?.Invoke();
            }

            this.Completed?.Invoke(snapshot.Status);
        }
        catch (Exception)
        {
            // Connection dropped; nothing opened.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost notice is harmless: the server forgets a viewer when the player leaves the place.")]
    private async Task SendCloseAsync(int objectId)
    {
        try
        {
            using PacketScope<CloseChest> request = PacketFactory<CloseChest>.Acquire();
            request.Value.ObjectId = objectId;
            await _socket.SendAsync(request.Value).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task MoveAsync(int objectId, ChestSide fromSide, int fromSlot, ChestSide toSide, int toSlot, int quantity)
    {
        try
        {
            using PacketScope<ChestMove> request = PacketFactory<ChestMove>.Acquire();
            request.Value.ObjectId = objectId;
            request.Value.FromSide = fromSide;
            request.Value.FromSlot = (byte)fromSlot;
            request.Value.ToSide = toSide;
            request.Value.ToSlot = (byte)toSlot;
            request.Value.Quantity = (ushort)Math.Max(0, quantity);
            ChestResult result = await _socket.RequestAsync<ChestResult>(request.Value).ConfigureAwait(false);
            if (result.Status is ChestStatus.OutOfReach or ChestStatus.NotYours or ChestStatus.UnknownObject)
            {
                this.Forget(objectId);
            }

            this.Completed?.Invoke(result.Status);
        }
        catch (Exception)
        {
            // Connection dropped; nothing moved.
        }
    }

    private void OnUpdate(ChestUpdate update)
    {
        lock (_gate)
        {
            if (update.ObjectId != _objectId)
            {
                return;
            }

            this.Apply(update.Slots);
        }

        this.Changed?.Invoke();
    }

    private void OnClosed(ChestClosed closed) => this.Forget(closed.ObjectId);

    private void Forget(int objectId)
    {
        lock (_gate)
        {
            if (_objectId != objectId)
            {
                return;
            }

            _objectId = 0;
            Array.Clear(_slots);
        }

        this.Closed?.Invoke();
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;

namespace Nosum.Wasm.Socket.Inventory;

/// <summary>One slot as the client mirrors it. Item id zero is an empty slot.</summary>
/// <param name="ItemId">Item id from the item catalogue, or zero.</param>
/// <param name="Quantity">How many are stacked.</param>
public readonly record struct SlotInfo(int ItemId, int Quantity);

/// <summary>
/// The client's mirror of the player's inventory: loads the snapshot, applies the server's pushed
/// updates, and sends slot selection and moves. The server owns the items; a move only shows once the
/// server's update arrives. Any feature that changes items (farming, market, quests) reaches the
/// mirror the same way, through <see cref="InventoryUpdate"/>.
/// </summary>
public sealed class InventoryChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable _updates;
    private readonly Lock _gate = new();
    private readonly SlotInfo[] _slots = new SlotInfo[255];
    private int _capacity;
    private int _selectedSlot;

    /// <summary>Initializes the channel and starts listening for pushed updates.</summary>
    /// <param name="socket">The game connection.</param>
    public InventoryChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
        _updates = socket.Subscribe<InventoryUpdate>(this.OnUpdate);
    }

    /// <summary>Raised when slots or the selection changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised when the server refused a move or a selection. May run on any thread.</summary>
    public event Action<InventoryStatus>? Refused;

    /// <summary>Gets the number of slots; zero until the snapshot arrived.</summary>
    public int Capacity
    {
        get
        {
            lock (_gate)
            {
                return _capacity;
            }
        }
    }

    /// <summary>Gets the hotbar slot the player holds.</summary>
    public int SelectedSlot
    {
        get
        {
            lock (_gate)
            {
                return _selectedSlot;
            }
        }
    }

    /// <summary>Reads a slot.</summary>
    /// <param name="slot">Slot number.</param>
    /// <returns>The slot's content; empty outside the inventory.</returns>
    public SlotInfo SlotAt(int slot)
    {
        lock (_gate)
        {
            return (uint)slot < (uint)_capacity ? _slots[slot] : default;
        }
    }

    /// <summary>Counts the total quantity of <paramref name="itemId"/> across all inventory slots.</summary>
    /// <param name="itemId">Item id from the item catalogue.</param>
    /// <returns>Total stacked quantity of the item carried by the player.</returns>
    public int CountOf(int itemId)
    {
        if (itemId <= 0)
        {
            return 0;
        }

        lock (_gate)
        {
            int total = 0;
            for (int i = 0; i < _capacity; i++)
            {
                if (_slots[i].ItemId == itemId)
                {
                    total += _slots[i].Quantity;
                }
            }

            return total;
        }
    }

    /// <summary>Loads the whole inventory. Call it whenever the connection is signed in; a new player gets the starter kit.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <summary>Selects a hotbar slot: shown at once, confirmed (or corrected) by the server.</summary>
    /// <param name="slot">Hotbar slot, zero to eight.</param>
    public void Select(int slot)
    {
        lock (_gate)
        {
            if (_capacity == 0 || (uint)slot >= 9u || slot == _selectedSlot)
            {
                return;
            }

            _selectedSlot = slot;
        }

        this.Changed?.Invoke();
        _ = this.SelectAsync(slot);
    }

    /// <summary>Asks the server to move a stack; the result arrives as an update.</summary>
    /// <param name="from">Source slot.</param>
    /// <param name="to">Target slot.</param>
    /// <param name="quantity">How many; zero moves the whole stack.</param>
    public void Move(int from, int to, int quantity = 0) => _ = this.MoveAsync(from, to, quantity);

    /// <inheritdoc/>
    public void Dispose() => _updates.Dispose();

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried at the next sign-in; there is no caller to report to.")]
    private async Task LoadAsync()
    {
        try
        {
            using PacketScope<GetInventory> request = PacketFactory<GetInventory>.Acquire();
            InventorySnapshot snapshot = await _socket.RequestAsync<InventorySnapshot>(request.Value).ConfigureAwait(false);
            lock (_gate)
            {
                Array.Clear(_slots);
                _capacity = snapshot.Capacity;
                _selectedSlot = snapshot.SelectedSlot;
                foreach (InventorySlotRow row in snapshot.Slots)
                {
                    if (row.Slot < _capacity)
                    {
                        _slots[row.Slot] = new SlotInfo(row.ItemId, row.Quantity);
                    }
                }
            }

            this.Changed?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost selection is corrected by the next snapshot.")]
    private async Task SelectAsync(int slot)
    {
        try
        {
            using PacketScope<SelectSlot> request = PacketFactory<SelectSlot>.Acquire();
            request.Value.Slot = (byte)slot;
            InventoryResult result = await _socket.RequestAsync<InventoryResult>(request.Value).ConfigureAwait(false);
            if (result.Status != InventoryStatus.Ok)
            {
                lock (_gate)
                {
                    _selectedSlot = result.SelectedSlot;
                }

                this.Changed?.Invoke();
                this.Refused?.Invoke(result.Status);
            }
        }
        catch (Exception)
        {
            // Connection dropped; the snapshot after the next sign-in carries the stored selection.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost move changes nothing; the player tries again.")]
    private async Task MoveAsync(int from, int to, int quantity)
    {
        try
        {
            using PacketScope<MoveSlot> request = PacketFactory<MoveSlot>.Acquire();
            request.Value.From = (byte)from;
            request.Value.To = (byte)to;
            request.Value.Quantity = quantity;
            InventoryResult result = await _socket.RequestAsync<InventoryResult>(request.Value).ConfigureAwait(false);
            if (result.Status != InventoryStatus.Ok)
            {
                this.Refused?.Invoke(result.Status);
            }
        }
        catch (Exception)
        {
            // See above.
        }
    }

    private void OnUpdate(InventoryUpdate update)
    {
        lock (_gate)
        {
            foreach (InventorySlotRow row in update.Slots)
            {
                if (row.Slot < _capacity)
                {
                    _slots[row.Slot] = new SlotInfo(row.ItemId, row.Quantity);
                }
            }
        }

        this.Changed?.Invoke();
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Inventory;

namespace Nosum.Wasm.Game;

/// <summary>
/// What the player's one-shot keys do: number keys select a hotbar slot, the action key (E or Space) uses the
/// held item on the tile in front of the farmer, I opens the inventory. The scene calls <see cref="Update"/> once per
/// frame; the tile in front is kept in <see cref="TargetX"/> and <see cref="TargetY"/> for the cursor the farm decorator draws.
/// Allocation-free; the requests themselves are sent by the channels, off the frame.
/// </summary>
internal sealed class PlayerActions
{
    // A short cooldown keeps a mashed key inside the server's rate limit.
    private const double UseCooldownSeconds = 0.2;

    private readonly FarmChannel _farm;
    private readonly InventoryChannel _inventory;
    private double _lastUseSeconds = double.NegativeInfinity;

    /// <summary>Initializes the actions.</summary>
    /// <param name="farm">Sends the use of an item.</param>
    /// <param name="inventory">Holds the selected slot.</param>
    public PlayerActions(FarmChannel farm, InventoryChannel inventory)
    {
        ArgumentNullException.ThrowIfNull(farm);
        ArgumentNullException.ThrowIfNull(inventory);
        _farm = farm;
        _inventory = inventory;
    }

    /// <summary>Raised (from the frame) when the inventory key was pressed.</summary>
    public event Action? InventoryToggleRequested;

    /// <summary>Gets the tile x in front of the farmer.</summary>
    public int TargetX { get; private set; }

    /// <summary>Gets the tile y in front of the farmer.</summary>
    public int TargetY { get; private set; }

    /// <summary>Handles the keys pressed this frame.</summary>
    /// <param name="edges">Keys pressed since the previous frame.</param>
    /// <param name="feetX">Farmer's feet x, world pixels.</param>
    /// <param name="feetY">Farmer's feet y, world pixels.</param>
    /// <param name="facing">Where the farmer looks.</param>
    /// <param name="tileSize">Tile size, pixels.</param>
    /// <param name="mapIsFarmable">Whether the map being shown can be farmed (otherwise the action key does nothing).</param>
    /// <param name="nowSeconds">Scene clock, seconds.</param>
    public void Update(InputEdges edges, double feetX, double feetY, Facing facing, int tileSize, bool mapIsFarmable, double nowSeconds)
    {
        int x = (int)(feetX / tileSize);
        int y = (int)(feetY / tileSize);
        (this.TargetX, this.TargetY) = facing switch
        {
            Facing.Up => (x, y - 1),
            Facing.Left => (x - 1, y),
            Facing.Right => (x + 1, y),
            _ => (x, y + 1),
        };

        if (edges == InputEdges.None)
        {
            return;
        }

        int slot = InputPacking.NumberSlot(edges);
        if (slot >= 0)
        {
            _inventory.Select(slot);
        }

        if ((edges & InputEdges.Inventory) != 0)
        {
            this.InventoryToggleRequested?.Invoke();
        }

        if ((edges & InputEdges.Action) != 0 && mapIsFarmable && nowSeconds - _lastUseSeconds >= UseCooldownSeconds)
        {
            _lastUseSeconds = nowSeconds;
            _farm.Use(_inventory.SelectedSlot, this.TargetX, this.TargetY);
        }
    }
}

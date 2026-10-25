// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Farm;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.ViewModels;

namespace Nosum.Wasm.Game;

/// <summary>
/// What the player's one-shot keys do: number keys select a hotbar slot, the action key (E or Space) uses the
/// held item on the tile in front of the farmer (or ships it when that tile is the shipping bin), I M C T G N open the panels; next to a shop the action key opens it. The scene calls <see cref="Update"/> once per
/// frame; the tile in front is kept in <see cref="TargetX"/> and <see cref="TargetY"/> for the cursor the farm decorator draws.
/// Allocation-free; the requests themselves are sent by the channels, off the frame.
/// </summary>
internal sealed class PlayerActions
{
    // A short cooldown keeps a mashed key inside the server's rate limit.
    private const double UseCooldownSeconds = 0.2;

    private readonly FarmChannel _farm;
    private readonly InventoryChannel _inventory;
    private readonly MarketChannel _market;
    private double _lastUseSeconds = double.NegativeInfinity;

    /// <summary>Initializes the actions.</summary>
    /// <param name="farm">Sends the use of an item.</param>
    /// <param name="inventory">Holds the selected slot.</param>
    /// <param name="market">Sends the shipping of the held stack at the bin.</param>
    public PlayerActions(FarmChannel farm, InventoryChannel inventory, MarketChannel market)
    {
        ArgumentNullException.ThrowIfNull(farm);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(market);
        _farm = farm;
        _inventory = inventory;
        _market = market;
    }

    /// <summary>Raised (from the frame) when a panel key was pressed: the panel to show or hide.</summary>
    public event Action<PanelKind>? PanelToggleRequested;

    /// <summary>Raised (from the frame) when the action key was pressed next to a shop: the shop's id (docs/adr/ADR-013 amendments).</summary>
    public event Action<int>? ShopRequested;

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
    /// <param name="shopId">The id of the shop whose counter is within reach, or zero; the action key then opens that shop instead of using an item.</param>
    public void Update(InputEdges edges, double feetX, double feetY, Facing facing, int tileSize, bool mapIsFarmable, double nowSeconds, int shopId = 0)
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

        this.RaisePanelKeys(edges);

        if ((edges & InputEdges.Action) != 0 && shopId != 0)
        {
            this.ShopRequested?.Invoke(shopId);
            return;
        }

        if ((edges & InputEdges.Action) != 0 && mapIsFarmable && nowSeconds - _lastUseSeconds >= UseCooldownSeconds)
        {
            _lastUseSeconds = nowSeconds;
            int selected = _inventory.SelectedSlot;
            SlotInfo held = _inventory.SlotAt(selected);
            if (IsShippingBin(this.TargetX, this.TargetY) && ShippingPrices.TryGet(held.ItemId, out _))
            {
                // Facing the bin with a crop in hand: sell the stack (the server checks the distance and prices it).
                _market.Ship(selected, held.ItemId, 0);
            }
            else
            {
                _farm.Use(selected, this.TargetX, this.TargetY);
            }
        }
    }

    // Whether a tile is one of the shipping bin's footprint tiles on the standard farm layout.
    private static bool IsShippingBin(int tileX, int tileY)
    {
        foreach (PropPlacement placement in FarmProps.Placements)
        {
            if (ReferenceEquals(placement.Definition, FarmProps.ShippingBin)
                && tileX >= placement.TileX && tileX < placement.TileX + placement.Definition.FootprintWidth
                && tileY >= placement.TileY && tileY < placement.TileY + placement.Definition.FootprintHeight)
            {
                return true;
            }
        }

        return false;
    }

    private void RaisePanelKeys(InputEdges edges)
    {
        if ((edges & InputEdges.Inventory) != 0)
        {
            this.PanelToggleRequested?.Invoke(PanelKind.Inventory);
        }

        if ((edges & InputEdges.Market) != 0)
        {
            this.PanelToggleRequested?.Invoke(PanelKind.Market);
        }

        if ((edges & InputEdges.Wardrobe) != 0)
        {
            this.PanelToggleRequested?.Invoke(PanelKind.Wardrobe);
        }

        if ((edges & InputEdges.Trade) != 0)
        {
            this.PanelToggleRequested?.Invoke(PanelKind.Trade);
        }

        if ((edges & InputEdges.Coop) != 0)
        {
            this.PanelToggleRequested?.Invoke(PanelKind.Coop);
        }

        if ((edges & InputEdges.Profile) != 0)
        {
            this.PanelToggleRequested?.Invoke(PanelKind.Profile);
        }
    }
}

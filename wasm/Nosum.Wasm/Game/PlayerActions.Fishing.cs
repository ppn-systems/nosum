// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Items;

namespace Nosum.Wasm.Game;

/// <summary>
/// The fishing rod in hand (docs/adr/ADR-071 D2, D9). A rod is cast at water by a click, never used on a tile: the action key does nothing with it.
/// The scene decides which tile was water; the fishing view model decides whether a line can go out now; the server checks the rest.
/// </summary>
internal sealed partial class PlayerActions
{
    /// <summary>Raised (from the frame) when a click landed on water while a fishing rod is held: the tile the line goes into.</summary>
    public event Action<int, int>? CastRequested;

    /// <summary>Gets whether the item in the selected hotbar slot is a fishing rod.</summary>
    public bool HoldsRod => IsFishingRod(this.HeldItemId);

    /// <summary>Asks for a cast at a water tile, as if the rod had been clicked on it. Does nothing when no rod is held.</summary>
    /// <param name="tileX">Tile x of the water.</param>
    /// <param name="tileY">Tile y of the water.</param>
    public void RequestCast(int tileX, int tileY)
    {
        if (this.HoldsRod)
        {
            this.CastRequested?.Invoke(tileX, tileY);
        }
    }

    // A fishing rod is a tool of its own kind (ToolKind.FishingRod): the farm's use intent and the swing never see it.
    private static bool IsFishingRod(int itemId) => ItemCatalog.TryGet(itemId, out ItemDefinition? item) && item.Tool == ToolKind.FishingRod;
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.ViewModels.Settings;

/// <summary>An action the player can bind a key to (docs/adr/ADR-033 D1). The names are the ones <c>renderer.js</c> maps to input bits.</summary>
public enum GameAction
{
    /// <summary>Walk up.</summary>
    MoveUp,

    /// <summary>Walk down.</summary>
    MoveDown,

    /// <summary>Walk left.</summary>
    MoveLeft,

    /// <summary>Walk right.</summary>
    MoveRight,

    /// <summary>Use the held item on the tile in front, talk, open.</summary>
    Use,

    /// <summary>Open the inventory.</summary>
    Inventory,

    /// <summary>Open the wardrobe.</summary>
    Wardrobe,

    /// <summary>Open the trade window.</summary>
    Trade,

    /// <summary>Open the co-op.</summary>
    Coop,

    /// <summary>Open the profile.</summary>
    Profile,

    /// <summary>Open the settings page.</summary>
    Help,

    /// <summary>Turn the furniture held.</summary>
    Rotate,

    /// <summary>Pick the furniture in front up to move it.</summary>
    Grab,

    /// <summary>Take the furniture in front back into the inventory.</summary>
    PickUp,
    /// <summary>Show or hide the minimap in the HUD corner.</summary>
    Minimap,
    /// <summary>Open or close the world map panel.</summary>
    WorldMap,
    /// <summary>Open or close the collection journal.</summary>
    Journal,
}

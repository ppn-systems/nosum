// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Placeables;

/// <summary>The groups the furniture catalogue panel sorts furniture into (the panel's category tabs).</summary>
public enum FurnitureCategory : byte
{
    /// <summary>Chairs, stools, benches and armchairs.</summary>
    Seating = 0,

    /// <summary>Tables and desks.</summary>
    Tables = 1,

    /// <summary>Beds.</summary>
    Beds = 2,

    /// <summary>Wardrobes, shelves, bookcases and cabinets.</summary>
    Storage = 3,

    /// <summary>Lamps and candles; they light the night.</summary>
    Lighting = 4,

    /// <summary>Potted plants and flowers.</summary>
    Plants = 5,

    /// <summary>Paintings, mirrors, clocks, windows and seasonal wall decorations.</summary>
    Decor = 6,

    /// <summary>Rugs and mats: walked over, never blocking.</summary>
    Rugs = 7,

    /// <summary>Crafted machines a player places on the farm: the furnace and the sprinkler (docs/adr/ADR-024). Listed with the furniture so the
    /// arrange panel shows the piece in hand (docs/design/critique-placement.md, finding 14).</summary>
    Equipment = 8,
}

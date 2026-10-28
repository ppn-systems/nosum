// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Farm;

/// <summary>What a successful <see cref="C2S.UseItem"/> did. Values are append-only.</summary>
public enum FarmAction : byte
{
    /// <summary>Nothing happened (the request was refused).</summary>
    None = 0,

    /// <summary>The soil was tilled.</summary>
    Tilled = 1,

    /// <summary>A seed was planted.</summary>
    Planted = 2,

    /// <summary>The soil was watered.</summary>
    Watered = 3,

    /// <summary>A crop was harvested.</summary>
    Harvested = 4,

    /// <summary>A resource node was hit and still stands.</summary>
    Hit = 5,

    /// <summary>A resource node broke (a tree falls to a stump, a stone is gone) and its yield was delivered.</summary>
    Broke = 6,

    /// <summary>The watering can was refilled at water.</summary>
    Refilled = 7,

    /// <summary>Tilled soil was cleared with a pickaxe.</summary>
    Cleared = 8,

    /// <summary>A tree seed was planted.</summary>
    PlantedTree = 9,

    /// <summary>A ladder was used: the player went down to the next mine floor (docs/adr/ADR-026 D3); the map change arrives as <c>MapChanged</c>.</summary>
    Descended = 10,
}

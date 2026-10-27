// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Npcs;

/// <summary>What a player can ask of a merchant in the dialog box (docs/adr/ADR-043 D3). Leave is always there and is not listed.</summary>
[Flags]
public enum NpcActions
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Buy: opens what the counter sells (a shop's list, or the market's listings).</summary>
    Buy = 1,

    /// <summary>Sell: opens the market's page for listing a stack.</summary>
    Sell = 2,

    /// <summary>Talk: another line from the merchant.</summary>
    Talk = 4,

    /// <summary>Build: the carpenter's buildings and animals.</summary>
    Build = 8,

    /// <summary>Upgrade: the blacksmith's tool upgrades.</summary>
    Upgrade = 16,

    /// <summary>Goals: the Town board's community goals and requests.</summary>
    Goals = 32,
}

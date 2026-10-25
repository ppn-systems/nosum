// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Items;

/// <summary>
/// How good a tool is (docs/adr/ADR-026 D1). Each tier of a tool is its own item id (<see cref="ItemCatalog"/>), so what a player holds says
/// the tier and nothing else has to be stored. The numbers are part of the data model; gold and iridium are appended later.
/// </summary>
public enum ToolTier : byte
{
    /// <summary>The tool a new player starts with.</summary>
    Basic = 0,

    /// <summary>Upgraded once at the blacksmith with copper bars.</summary>
    Copper = 1,

    /// <summary>Upgraded twice, with iron bars.</summary>
    Iron = 2,
}

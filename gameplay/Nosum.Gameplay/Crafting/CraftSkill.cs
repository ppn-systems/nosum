// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Crafting;

/// <summary>
/// The skills a recipe can be unlocked by (docs/adr/ADR-023 D3, docs/adr/ADR-024 D1). The numbers are wire values of the recipe book;
/// append-only.
/// </summary>
public enum CraftSkill : byte
{
    /// <summary>No skill: the recipe is known from the start.</summary>
    None = 0,

    /// <summary>Tilling, watering and harvesting.</summary>
    Farming = 1,

    /// <summary>Chopping and cutting.</summary>
    Foraging = 2,

    /// <summary>Breaking stones and ore.</summary>
    Mining = 3,
}

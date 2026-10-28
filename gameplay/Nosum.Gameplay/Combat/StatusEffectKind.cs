// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Combat;

/// <summary>Status effect kinds inflicted by potions, elixirs, and monster attacks (docs/adr/ADR-051). Append-only.</summary>
public enum StatusEffectKind : byte
{
    /// <summary>No active effect.</summary>
    None = 0,

    /// <summary>Gradually restores HP over time.</summary>
    Regeneration = 1,

    /// <summary>Gradually deals poison/corrosive damage over time.</summary>
    Poison = 2,

    /// <summary>Enhances agility and movement speed.</summary>
    Swiftness = 3,
}

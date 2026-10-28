// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Combat;

/// <summary>
/// Elite monster affix modifier giving unique passives, tints, and bonuses (docs/adr/ADR-058).
/// </summary>
public enum MonsterModifier : byte
{
    /// <summary>Standard non-elite monster.</summary>
    None = 0,

    /// <summary>Fiery damage reflection and burning aura.</summary>
    FlameAura = 1,

    /// <summary>Fast movement speed and rapid pursuit.</summary>
    Swift = 2,

    /// <summary>Hardened defense mitigation and expanded health pool.</summary>
    Shielded = 3,

    /// <summary>Life-stealing contact attack that restores health on hit.</summary>
    Vampiric = 4,
}

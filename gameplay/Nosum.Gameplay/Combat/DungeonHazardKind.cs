// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Combat;

/// <summary>
/// Types of environmental hazards present in mine caverns and dungeons (docs/adr/ADR-061).
/// </summary>
public enum DungeonHazardKind : byte
{
    /// <summary>No hazard.</summary>
    None = 0,

    /// <summary>Fungal spore cloud: periodic poisonous gas ticks in shallow caves (floors 1-10).</summary>
    SporeVent = 1,

    /// <summary>Mechanical spike trap: periodically springs steel spikes in mid dungeon floors (floors 11-20).</summary>
    SpikeTrap = 2,

    /// <summary>Magma geyser fissure: erupts fiery bursts in deep subterranean depths (floors 21-40).</summary>
    MagmaFissure = 3,
}

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

    /// <summary>Ancient Vitality Fountain: restores HP and grants Regeneration aura (docs/adr/ADR-063).</summary>
    VitalityShrine = 4,

    /// <summary>Ancient Arcane Obelisk: restores MP and awards Combat XP (docs/adr/ADR-063).</summary>
    ArcaneObelisk = 5,

    /// <summary>Ancient Sanctuary Altar: restores HP and MP, grants Swiftness, and awards Combat XP on milestone floors (docs/adr/ADR-063).</summary>
    EmpowermentAltar = 6,
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Combat;

/// <summary>
/// An environmental hazard instance on a dungeon floor or map (docs/adr/ADR-061).
/// </summary>
/// <param name="HazardId">Unique identifier of the hazard on the instance.</param>
/// <param name="Kind">The hazard type.</param>
/// <param name="X">Tile X coordinate.</param>
/// <param name="Y">Tile Y coordinate.</param>
/// <param name="Damage">Base damage dealt per active contact tick.</param>
public readonly record struct DungeonHazard(
    byte HazardId,
    DungeonHazardKind Kind,
    short X,
    short Y,
    byte Damage);

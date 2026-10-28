// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Combat;

/// <summary>A spawned monster on a map or mine floor.</summary>
/// <param name="MonsterId">Stable id within the map/floor instance (1..N).</param>
/// <param name="Kind">The monster species.</param>
/// <param name="X">Tile X.</param>
/// <param name="Y">Tile Y.</param>
/// <param name="Hp">Current HP.</param>
/// <param name="MaxHp">Maximum HP.</param>
/// <param name="Modifier">Elite affix modifier (docs/adr/ADR-058).</param>
public readonly record struct MonsterSpawn(int MonsterId, MonsterKind Kind, int X, int Y, int Hp, int MaxHp, MonsterModifier Modifier = MonsterModifier.None);

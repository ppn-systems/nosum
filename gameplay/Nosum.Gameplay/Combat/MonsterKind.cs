// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Combat;

/// <summary>
/// Which monster species a spawn is (docs/adr/ADR-045 D3). The byte values are also wire values and sprite block indices (minus one) in
/// <c>assets/sprites/monsters.png</c>; append-only.
/// </summary>
public enum MonsterKind : byte
{
    /// <summary>No monster.</summary>
    None = 0,

    /// <summary>Green slime found in the upper mine and woods.</summary>
    GreenSlime = 1,

    /// <summary>Frost slime found in the mid mine and cold caverns.</summary>
    FrostSlime = 2,

    /// <summary>Magma slime found in the deep mine.</summary>
    MagmaSlime = 3,

    /// <summary>Cave bat fluttering through mine shafts.</summary>
    CaveBat = 4,

    /// <summary>Spore shroom lurking in damp caverns and woods.</summary>
    SporeShroom = 5,

    /// <summary>Goblin scout patrolling mountain passes and mid-level mines.</summary>
    GoblinScout = 6,

    /// <summary>Skeleton warrior guarding deep dungeon floors.</summary>
    SkeletonWarrior = 7,

    /// <summary>Stone golem animated by ancient mana cores.</summary>
    StoneGolem = 8,
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Combat;

/// <summary>Outcome of a combat or potion request (docs/adr/ADR-045 D3). Wire values are append-only.</summary>
public enum CombatStatus : byte
{
    /// <summary>The action succeeded.</summary>
    Ok = 0,

    /// <summary>The player has not joined a map yet.</summary>
    NotJoined = 1,

    /// <summary>The target monster does not exist or is already defeated.</summary>
    UnknownMonster = 2,

    /// <summary>The monster is outside the held weapon's attack range.</summary>
    OutOfRange = 3,

    /// <summary>The selected slot does not hold a weapon or staff (for attacks) or a potion (for drinking).</summary>
    WrongItem = 4,

    /// <summary>The player does not have enough MP to cast with a staff.</summary>
    NotEnoughMp = 5,

    /// <summary>The player's HP and MP are already full.</summary>
    AlreadyFull = 6,
}

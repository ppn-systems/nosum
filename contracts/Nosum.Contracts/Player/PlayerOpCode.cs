// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Player;

/// <summary>
/// Opcodes of the Player domain (presence and movement).
/// <para>Band <c>0x2000-0x20FF</c> is reserved for this domain; <c>0x0000-0x00FF</c> belongs to Nalix and <c>0x1000-0x10FF</c> to Account, <c>0x7000-0x70FF</c> to World.
/// C2S requests use <c>0x2000-0x203F</c>, S2C replies and pushes use <c>0x2040-0x207F</c>.
/// Never reassign or reuse a value.</para>
/// </summary>
public static class PlayerOpCode
{
    // -- C2S (0x2000-0x203F) --------------------------------------------------

    /// <summary>Asks to enter a map. Reply: <see cref="S2C.JoinMapResult"/>.</summary>
    public const ushort JoinMap = 0x2000;

    /// <summary>Reports where the player's feet are now. No reply; a refusal arrives as <see cref="S2C.MoveCorrection"/>.</summary>
    public const ushort MoveIntent = 0x2001;

    /// <summary>Eats the food in a hotbar slot. Reply: <see cref="S2C.EatResult"/> (docs/adr/ADR-023).</summary>
    public const ushort Eat = 0x2002;

    // -- S2C (0x2040-0x207F) --------------------------------------------------

    /// <summary>Reply to <see cref="JoinMap"/>.</summary>
    public const ushort JoinMapResult = 0x2040;

    /// <summary>Push: the full state of every player on the map, about 10 times per second.</summary>
    public const ushort MapSnapshot = 0x2041;

    /// <summary>Push: a player left the map.</summary>
    public const ushort PlayerLeft = 0x2042;

    /// <summary>Push to one player: the server refused a <see cref="MoveIntent"/> and puts the player back.</summary>
    public const ushort MoveCorrection = 0x2043;

    /// <summary>Reply to <see cref="Eat"/>.</summary>
    public const ushort EatResult = 0x2044;

    /// <summary>Push to one player: their energy changed (docs/adr/ADR-023).</summary>
    public const ushort EnergyChanged = 0x2045;

    /// <summary>Push to one player: their skill experience totals changed.</summary>
    public const ushort SkillXpChanged = 0x2046;

    /// <summary>Push to one player: a skill reached a new level.</summary>
    public const ushort LevelUp = 0x2047;
}

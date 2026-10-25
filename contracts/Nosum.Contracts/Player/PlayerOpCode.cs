// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Player;

/// <summary>
/// Opcodes of the Player domain (presence and movement).
/// <para>Band <c>0x2000-0x20FF</c> is reserved for this domain; <c>0x0000-0x00FF</c> belongs to Nalix and <c>0x1000-0x10FF</c> to Account.
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

    // -- S2C (0x2040-0x207F) --------------------------------------------------

    /// <summary>Reply to <see cref="JoinMap"/>.</summary>
    public const ushort JoinMapResult = 0x2040;

    /// <summary>Push: the full state of every player on the map, about 10 times per second.</summary>
    public const ushort MapSnapshot = 0x2041;

    /// <summary>Push: a player left the map.</summary>
    public const ushort PlayerLeft = 0x2042;

    /// <summary>Push to one player: the server refused a <see cref="MoveIntent"/> and puts the player back.</summary>
    public const ushort MoveCorrection = 0x2043;
}

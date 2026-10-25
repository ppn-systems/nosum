// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Visit;

/// <summary>
/// Opcodes of the Visit domain: the allow-list of players who may enter an owner's farm and farmhouse, and the request to visit one (docs/adr/ADR-019).
/// <para>Band <c>0xE000-0xE0FF</c> is reserved for this domain. C2S requests use <c>0xE000-0xE03F</c>,
/// S2C replies use <c>0xE040-0xE07F</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class VisitOpCode
{
    // -- C2S (0xE000-0xE03F) --------------------------------------------------

    /// <summary>Asks for the player's own allow-list and the players now in their place. Reply: <see cref="S2C.VisitorsSnapshot"/>.</summary>
    public const ushort GetVisitors = 0xE000;

    /// <summary>Adds a player, by nickname, to the allow-list. Reply: <see cref="S2C.VisitResult"/>.</summary>
    public const ushort AddVisitor = 0xE001;

    /// <summary>Removes a player from the allow-list and sends them out of the place if they are in it. Reply: <see cref="S2C.VisitResult"/>.</summary>
    public const ushort RemoveVisitor = 0xE002;

    /// <summary>Sends a player now in the owner's place back to the Town, without removing them from the allow-list. Reply: <see cref="S2C.VisitResult"/>.</summary>
    public const ushort KickVisitor = 0xE003;

    /// <summary>Turns "co-op members may visit" on or off. Reply: <see cref="S2C.VisitResult"/>.</summary>
    public const ushort SetCoopVisits = 0xE004;

    /// <summary>Asks to visit another player's farm, by nickname. The server checks the allow-list; on success the player is moved and gets a <c>MapChanged</c> push. Reply: <see cref="S2C.VisitResult"/>.</summary>
    public const ushort VisitPlayer = 0xE005;

    // -- S2C (0xE040-0xE07F) --------------------------------------------------

    /// <summary>Reply to every Visit request except <see cref="GetVisitors"/>.</summary>
    public const ushort VisitResult = 0xE040;

    /// <summary>Reply to <see cref="GetVisitors"/>.</summary>
    public const ushort VisitorsSnapshot = 0xE041;
}

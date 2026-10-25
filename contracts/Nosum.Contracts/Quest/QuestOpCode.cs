// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Quest;

/// <summary>
/// Opcodes of the Quest domain.
/// <para>Band <c>0x6000-0x60FF</c> is reserved for this domain. C2S requests use <c>0x6000-0x603F</c>,
/// S2C replies and pushes use <c>0x6040-0x607F</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class QuestOpCode
{
    /// <summary>Asks for the player's quests. Reply: <see cref="S2C.QuestLog"/>.</summary>
    public const ushort GetQuests = 0x6000;

    /// <summary>Reply to <see cref="GetQuests"/>.</summary>
    public const ushort QuestLog = 0x6040;

    /// <summary>Push to one player: a quest's progress changed or it was completed.</summary>
    public const ushort QuestUpdate = 0x6041;
}

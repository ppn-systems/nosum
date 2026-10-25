// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Goals;

/// <summary>
/// Opcodes of the community goals (docs/adr/ADR-030 D5).
/// <para>Slice <c>0x4100-0x41FF</c> of the Market band (<see cref="Market.MarketOpCode"/> owns <c>0x4000-0x40FF</c>). C2S requests use the lower half, S2C replies and pushes the upper half. Never reassign or reuse a value.</para>
/// </summary>
public static class GoalOpCode
{
    // -- C2S --

    /// <summary>Reads the goals of the current season and the player's part in them. Reply: <see cref="S2C.GoalsSnapshot"/>.</summary>
    public const ushort GetGoals = 0x4100;

    /// <summary>Delivers items from the inventory into a goal at the Town board. Reply: <see cref="S2C.DeliverResult"/>.</summary>
    public const ushort DeliverToGoal = 0x4101;

    // -- S2C --

    /// <summary>Reply to <see cref="GetGoals"/>.</summary>
    public const ushort GoalsSnapshot = 0x4140;

    /// <summary>Reply to <see cref="DeliverToGoal"/>.</summary>
    public const ushort DeliverResult = 0x4141;

    /// <summary>Push to everyone in the Town: a goal's progress changed (a delivery, completion, expiry).</summary>
    public const ushort GoalProgress = 0x4142;
}

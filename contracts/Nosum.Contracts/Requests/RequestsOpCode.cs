// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Requests;

/// <summary>
/// Opcodes of the Town requests board (docs/adr/ADR-036 D8).
/// <para>Slice <c>0x6100-0x61FF</c> of the Quest band (<see cref="Quest.QuestOpCode"/> owns <c>0x6000-0x60FF</c>). C2S requests use the lower half, S2C replies the upper half. Never reassign or reuse a value.</para>
/// </summary>
public static class RequestsOpCode
{
    // -- C2S --

    /// <summary>Reads today's requests and which of them the player filled. Reply: <see cref="S2C.RequestsSnapshot"/>.</summary>
    public const ushort GetRequests = 0x6100;

    /// <summary>Delivers the items of one of today's requests at the Town board. Reply: <see cref="S2C.RequestResult"/>.</summary>
    public const ushort DeliverRequest = 0x6101;

    // -- S2C --

    /// <summary>Reply to <see cref="GetRequests"/>.</summary>
    public const ushort RequestsSnapshot = 0x6140;

    /// <summary>Reply to <see cref="DeliverRequest"/>.</summary>
    public const ushort RequestResult = 0x6141;
}

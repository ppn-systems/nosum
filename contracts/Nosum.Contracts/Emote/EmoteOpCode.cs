// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Emote;

/// <summary>
/// Opcodes of the Emote domain (docs/adr/ADR-075 D1-D2). Sub-range <c>0x9400-0x94FF</c> of the Trade band (docs/adr/ADR-029 D6). C2S requests use
/// <c>0x9400-0x943F</c>, S2C replies and pushes <c>0x9440-0x947F</c>. Never reassign or reuse a value.
/// </summary>
public static class EmoteOpCode
{
    // -- C2S --

    /// <summary>Shows an emote above the farmer for the players near them. Reply: <see cref="S2C.EmoteResult"/>.</summary>
    public const ushort SendEmote = 0x9400;

    // -- S2C --

    /// <summary>Reply to <c>SendEmote</c>.</summary>
    public const ushort EmoteResult = 0x9440;

    /// <summary>Push to every player who can see the sender: an emote to show above the sender.</summary>
    public const ushort EmoteShown = 0x9441;
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Emotes;

/// <summary>
/// The emote wheel (docs/adr/ADR-075 D1): twelve codes a player can show above their farmer for a few seconds. Only the code travels
/// the wire; the client draws the icon. Pure and shared by the server (which checks the code and the cooldown) and the client (which
/// draws the wheel and the bubble).
/// </summary>
public static class EmoteRules
{
    /// <summary>How many emotes the wheel holds; the codes are 0 to Count - 1, in the order the icons sheet holds them.</summary>
    public const int Count = 12;

    /// <summary>How long a bubble stays above the farmer, in milliseconds.</summary>
    public const int ShownMs = 3_000;

    /// <summary>The least time between two emotes of one player, in milliseconds.</summary>
    public const long MinIntervalMs = 1_500;

    /// <summary>Whether the code names one of the emotes on the wheel.</summary>
    /// <param name="emote">The code the client sent.</param>
    public static bool IsValid(byte emote) => emote < Count;
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Fishing;

/// <summary>
/// Opcodes of fishing (docs/adr/ADR-078): casting a fishing rod at water and reeling the line in. Band <c>0xC300-0xC3FF</c> (the Shop, Mine and
/// Combat bands sit below it). C2S requests use <c>0xC300-0xC33F</c>, S2C replies use <c>0xC340-0xC37F</c>. Never reassign or reuse a value.
/// </summary>
public static class FishingOpCode
{
    // -- C2S (0xC300-0xC33F) --------------------------------------------------

    /// <summary>Casts the held fishing rod at a water tile. Reply: <see cref="S2C.CastResult"/>.</summary>
    public const ushort CastRod = 0xC300;

    /// <summary>Reels the line in during the bite window. Reply: <see cref="S2C.ReelResult"/>.</summary>
    public const ushort ReelIn = 0xC301;

    // -- S2C (0xC340-0xC37F) --------------------------------------------------

    /// <summary>Reply to <see cref="CastRod"/>.</summary>
    public const ushort CastResult = 0xC340;

    /// <summary>Reply to <see cref="ReelIn"/>.</summary>
    public const ushort ReelResult = 0xC341;
}

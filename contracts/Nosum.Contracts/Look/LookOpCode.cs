// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Look;

/// <summary>
/// Opcodes of the Look domain.
/// <para>Avatar looks and the wardrobe (docs/adr/ADR-014). Band <c>0x8000-0x80FF</c> is reserved for this domain. C2S requests use the lower half, S2C replies and pushes the upper half. Never reassign or reuse a value.</para>
/// </summary>
public static class LookOpCode
{
    // -- C2S --

    /// <summary>Asks for the looks of everyone on the player's map and announces the player's own look to the rest of the map. Reply: <see cref="S2C.LooksSnapshot"/>.</summary>
    public const ushort GetLooks = 0x8000;

    /// <summary>Chooses skin, hair style and hair dye. Reply: <see cref="S2C.LookResult"/>; the change reaches the map as <see cref="S2C.LookChanged"/>.</summary>
    public const ushort SetAppearance = 0x8001;

    /// <summary>Wears a clothing item from an inventory slot. Reply: <see cref="S2C.LookResult"/>.</summary>
    public const ushort EquipItem = 0x8002;

    /// <summary>Takes a worn item off into the inventory. Reply: <see cref="S2C.LookResult"/>.</summary>
    public const ushort UnequipItem = 0x8003;

    // -- S2C --

    /// <summary>Reply to <see cref="GetLooks"/>.</summary>
    public const ushort LooksSnapshot = 0x8040;

    /// <summary>Reply to a look change.</summary>
    public const ushort LookResult = 0x8041;

    /// <summary>Push to every player of a map: one player's look (on join, map change and every change).</summary>
    public const ushort LookChanged = 0x8042;
}

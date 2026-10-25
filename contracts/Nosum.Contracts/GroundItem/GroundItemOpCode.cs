// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.GroundItem;

/// <summary>
/// Opcodes of the ground items: things lying in the world that a walking player picks up (docs/adr/ADR-021 D5-D6, D9).
/// <para>Sub-range <c>0x7200-0x72FF</c> of the World band is reserved for this domain. C2S requests use <c>0x7200-0x723F</c>,
/// S2C replies and pushes use <c>0x7240-0x727F</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class GroundItemOpCode
{
    // -- C2S (0x7200-0x723F) --------------------------------------------------

    /// <summary>Asks for every ground item of the instance the player is in. Reply: <see cref="S2C.GroundItemsSnapshot"/>.</summary>
    public const ushort GetGroundItems = 0x7200;

    // -- S2C (0x7240-0x727F) --------------------------------------------------

    /// <summary>Reply to <see cref="GetGroundItems"/>.</summary>
    public const ushort GroundItemsSnapshot = 0x7240;

    /// <summary>Push to the players of an instance: an item appeared on the ground.</summary>
    public const ushort GroundItemSpawned = 0x7241;

    /// <summary>Push to the players of an instance: an item left the ground (picked up or expired).</summary>
    public const ushort GroundItemTaken = 0x7242;
}

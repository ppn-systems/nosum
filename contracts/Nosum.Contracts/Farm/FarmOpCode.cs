// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Farm;

/// <summary>
/// Opcodes of the Farm domain (tilling, planting, watering, harvesting).
/// <para>Band <c>0x3000-0x30FF</c> is reserved for this domain. C2S requests use <c>0x3000-0x303F</c>,
/// S2C replies and pushes use <c>0x3040-0x307F</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class FarmOpCode
{
    // -- C2S (0x3000-0x303F) --------------------------------------------------

    /// <summary>Uses the item in a hotbar slot on a tile. Reply: <see cref="S2C.UseItemResult"/>; the tile change reaches everyone on the map as <see cref="S2C.FarmTilesChanged"/>.</summary>
    public const ushort UseItem = 0x3000;

    /// <summary>Asks for every worked tile of the map the player is on. Reply: <see cref="S2C.FarmSnapshot"/>.</summary>
    public const ushort GetFarm = 0x3001;

    /// <summary>
    /// Uses a hoe or a watering can on a line of tiles with one swing: the wide use of an upgraded tool (docs/adr/ADR-026 D1). Reply: <see cref="S2C.UseItemResult"/>.
    /// A new packet rather than a field on <see cref="UseItem"/>, so the existing packet and the protocol version stay as they are.
    /// </summary>
    public const ushort UseItemArea = 0x3002;

    // -- S2C (0x3040-0x307F) --------------------------------------------------

    /// <summary>Reply to <see cref="UseItem"/>.</summary>
    public const ushort UseItemResult = 0x3040;

    /// <summary>Reply to <see cref="GetFarm"/>.</summary>
    public const ushort FarmSnapshot = 0x3041;

    /// <summary>Push to every player of a map: some tiles changed.</summary>
    public const ushort FarmTilesChanged = 0x3042;
}

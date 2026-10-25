// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Shop;

/// <summary>
/// Opcodes of the Shop domain.
/// <para>Buying from a shop with coins (docs/adr/ADR-013 amendments). Band <c>0xC000-0xC0FF</c> is reserved for this domain. C2S requests use the lower half, S2C replies the upper half. Never reassign or reuse a value.</para>
/// </summary>
public static class ShopOpCode
{
    // -- C2S --

    /// <summary>Buys an item from a shop for coins. Reply: <see cref="S2C.ShopResult"/>; the items and coins arrive as the usual inventory and wallet pushes.</summary>
    public const ushort BuyFromShop = 0xC000;

    // -- S2C --

    /// <summary>Reply to <see cref="BuyFromShop"/>.</summary>
    public const ushort ShopResult = 0xC040;
}

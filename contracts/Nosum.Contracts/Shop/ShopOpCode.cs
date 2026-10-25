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

    /// <summary>Asks for the player's tool at the blacksmith, if any. Reply: <see cref="S2C.BlacksmithState"/>.</summary>
    public const ushort GetBlacksmith = 0xC001;

    /// <summary>Hands a tool to the blacksmith to be upgraded to the next tier for coins and bars (docs/adr/ADR-026 D2). Reply: <see cref="S2C.BlacksmithState"/>.</summary>
    public const ushort UpgradeTool = 0xC002;

    /// <summary>Collects the upgraded tool once the two game days are over. Reply: <see cref="S2C.BlacksmithState"/>.</summary>
    public const ushort CollectTool = 0xC003;

    // -- S2C --

    /// <summary>Reply to <see cref="BuyFromShop"/>.</summary>
    public const ushort ShopResult = 0xC040;

    /// <summary>Reply to <see cref="GetBlacksmith"/>, <see cref="UpgradeTool"/> and <see cref="CollectTool"/>: the outcome and the player's tool at the smith now.</summary>
    public const ushort BlacksmithState = 0xC041;
}

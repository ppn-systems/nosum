// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Market;

/// <summary>
/// Opcodes of the Market domain.
/// <para>Coins, the shipping bin and the consignment market (docs/adr/ADR-013). Band <c>0x4000-0x40FF</c> is reserved for this domain. C2S requests use the lower half, S2C replies and pushes the upper half. Never reassign or reuse a value.</para>
/// </summary>
public static class MarketOpCode
{
    // -- C2S --

    /// <summary>Asks for the coin balance. Reply: <see cref="S2C.WalletSnapshot"/>.</summary>
    public const ushort GetWallet = 0x4000;

    /// <summary>Sells items from an inventory slot at the shipping bin. Reply: <see cref="S2C.ShipResult"/>.</summary>
    public const ushort ShipItems = 0x4001;

    /// <summary>Reads one page of active listings. Reply: <see cref="S2C.ListingsPage"/>.</summary>
    public const ushort BrowseListings = 0x4002;

    /// <summary>Puts an item stack on the market (the items go into escrow). Reply: <see cref="S2C.MarketResult"/>.</summary>
    public const ushort CreateListing = 0x4003;

    /// <summary>Buys a listing. Reply: <see cref="S2C.MarketResult"/>.</summary>
    public const ushort BuyListing = 0x4004;

    /// <summary>Withdraws the player's own listing; the items come back. Reply: <see cref="S2C.MarketResult"/>.</summary>
    public const ushort CancelListing = 0x4005;

    /// <summary>Reads the player's own listings. Reply: <see cref="S2C.ListingsPage"/>.</summary>
    public const ushort GetMyListings = 0x4006;

    // -- S2C --

    /// <summary>Reply to <see cref="GetWallet"/>.</summary>
    public const ushort WalletSnapshot = 0x4040;

    /// <summary>Push to one player: the coin balance changed (a sale, a purchase, a trade).</summary>
    public const ushort WalletUpdate = 0x4041;

    /// <summary>Reply to <see cref="ShipItems"/>.</summary>
    public const ushort ShipResult = 0x4042;

    /// <summary>Reply to <see cref="BrowseListings"/> and <see cref="GetMyListings"/>.</summary>
    public const ushort ListingsPage = 0x4043;

    /// <summary>Reply to <see cref="CreateListing"/>, <see cref="BuyListing"/> and <see cref="CancelListing"/>.</summary>
    public const ushort MarketResult = 0x4044;
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Market;
using Nosum.Contracts.Market;
using Nosum.Contracts.Market.C2S;
using Nosum.Contracts.Market.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Market;

/// <summary>Coins, the shipping bin and the consignment market. The server prices, escrows and moves everything (docs/adr/ADR-013); a handler only relays an intent.</summary>
[PacketHandler("Nosum.Market")]
public static partial class MarketHandlers
{
    [Inject] private static IMarketService s_market = null!;
    [Inject] private static ITransferService s_transfers = null!;

    /// <summary>Replies with the coin balance.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(MarketOpCode.GetWallet)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 4, PolicyId = "wallet-read")]
    public static async ValueTask HandleGetWalletAsync(IPacketContext<GetWallet> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();
        long coins = await s_transfers.GetBalanceAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<GetWallet, WalletSnapshot>(context, sequence, r => r.Coins = coins).ConfigureAwait(false);
    }

    /// <summary>Sells items at the shipping bin.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(MarketOpCode.ShipItems)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(8, 4, PolicyId = "market-ship")]
    public static async ValueTask HandleShipItemsAsync(IPacketContext<ShipItems> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int slot = context.Packet.Slot;
        int itemId = context.Packet.ItemId;
        int quantity = context.Packet.Quantity;
        PlayerIdentity player = context.Connection.GetPlayer();

        ShipOutcome outcome = await s_market.ShipAsync(context.Connection, player.PlayerId, slot, itemId, quantity, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<ShipItems, ShipResult>(context, sequence, r =>
        {
            r.Status = outcome.Status;
            r.Earned = outcome.Earned;
        }).ConfigureAwait(false);
    }

    /// <summary>Reads one page of active listings.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(MarketOpCode.BrowseListings)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(8, 4, PolicyId = "market-browse")]
    public static async ValueTask HandleBrowseListingsAsync(IPacketContext<BrowseListings> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int page = context.Packet.Page;
        PlayerIdentity player = context.Connection.GetPlayer();

        ListingsPageView view = await s_market.BrowseAsync(page, context.Packet.ItemIds, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<BrowseListings, ListingsPage>(context, sequence, r => FillPage(r, view, player.PlayerId)).ConfigureAwait(false);
    }

    /// <summary>Reads the player's own listings.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(MarketOpCode.GetMyListings)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(8, 4, PolicyId = "market-mine")]
    public static async ValueTask HandleGetMyListingsAsync(IPacketContext<GetMyListings> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        ListingsPageView view = await s_market.MineAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<GetMyListings, ListingsPage>(context, sequence, r => FillPage(r, view, player.PlayerId)).ConfigureAwait(false);
    }

    /// <summary>Reads the daily price history of one item (docs/adr/ADR-030 D4).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(MarketOpCode.GetPriceHistory)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(8, 4, PolicyId = "market-history")]
    public static async ValueTask HandleGetPriceHistoryAsync(IPacketContext<GetPriceHistory> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int itemId = context.Packet.ItemId;

        PriceHistoryView view = await s_market.GetPriceHistoryAsync(itemId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<GetPriceHistory, PriceHistory>(context, sequence, r =>
        {
            r.ItemId = view.ItemId;
            r.Today = view.Today;
            r.Days.Clear();
            foreach (PriceDayView day in view.Days)
            {
                PriceDayRow row = PriceDayRow.Create();
                row.Day = day.Day;
                row.Min = day.Min;
                row.Median = day.Median;
                row.Max = day.Max;
                row.Sales = day.Sales;
                r.Days.Add(row);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Puts a stack on the market.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(MarketOpCode.CreateListing)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(8, 4, PolicyId = "market-write")]
    public static async ValueTask HandleCreateListingAsync(IPacketContext<CreateListing> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int slot = context.Packet.Slot;
        int itemId = context.Packet.ItemId;
        int quantity = context.Packet.Quantity;
        long price = context.Packet.Price;
        PlayerIdentity player = context.Connection.GetPlayer();

        MarketStatus status = await s_market.CreateListingAsync(context.Connection, player.PlayerId, slot, itemId, quantity, price, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<CreateListing, MarketResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Buys a listing.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(MarketOpCode.BuyListing)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(8, 4, PolicyId = "market-write")]
    public static async ValueTask HandleBuyListingAsync(IPacketContext<BuyListing> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        Guid listingId = context.Packet.ListingId;
        PlayerIdentity player = context.Connection.GetPlayer();

        MarketStatus status = await s_market.BuyAsync(context.Connection, player.PlayerId, listingId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<BuyListing, MarketResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Withdraws the player's own listing.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(MarketOpCode.CancelListing)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(8, 4, PolicyId = "market-write")]
    public static async ValueTask HandleCancelListingAsync(IPacketContext<CancelListing> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        Guid listingId = context.Packet.ListingId;
        PlayerIdentity player = context.Connection.GetPlayer();

        MarketStatus status = await s_market.CancelAsync(context.Connection, player.PlayerId, listingId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<CancelListing, MarketResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    private static void FillPage(ListingsPage page, ListingsPageView view, Guid viewer)
    {
        page.ServerNowMs = view.ServerNow.ToUnixTimeMilliseconds();
        page.Page = view.Page;
        page.Total = view.Total;
        page.Rows.Clear();
        foreach (ListingView listing in view.Listings)
        {
            ListingRow row = ListingRow.Create();
            row.ListingId = listing.ListingId;
            row.ItemId = listing.ItemId;
            row.Quantity = listing.Quantity;
            row.Price = listing.Price;
            row.ExpiresAtMs = listing.ExpiresAt.ToUnixTimeMilliseconds();
            row.IsMine = listing.SellerId == viewer;
            row.SellerNickname = listing.SellerNickname;
            page.Rows.Add(row);
        }
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Market;
using Nosum.Contracts.Market.C2S;
using Nosum.Contracts.Market.S2C;

namespace Nosum.Wasm.Socket.Market;

/// <summary>One listing as the client mirrors it. <paramref name="IsMine"/> says whether the viewer is the seller; the seller is shown by nickname.</summary>
/// <param name="Id">The listing id.</param>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">How many.</param>
/// <param name="Price">Price for the whole stack, coins.</param>
/// <param name="ExpiresAtMs">Expiry, Unix milliseconds on the server clock.</param>
/// <param name="IsMine">Whether the viewer is the seller.</param>
/// <param name="SellerNickname">The seller's nickname as the server sent it (a guest's neutral name is localized by the view model).</param>
public readonly record struct ListingInfo(Guid Id, int ItemId, int Quantity, long Price, long ExpiresAtMs, bool IsMine, string SellerNickname);

/// <summary>One page of listings with the server clock it was read at.</summary>
/// <param name="ServerNowMs">The server clock, Unix milliseconds.</param>
/// <param name="Page">Page number, zero based.</param>
/// <param name="Total">Number of matching listings, all pages.</param>
/// <param name="Listings">The page.</param>
public sealed record ListingsInfo(long ServerNowMs, int Page, int Total, IReadOnlyList<ListingInfo> Listings)
{
    /// <summary>Gets an empty page.</summary>
    public static readonly ListingsInfo Empty = new(0, 0, 0, []);
}

/// <summary>The sales of one item on one game day.</summary>
/// <param name="Day">The game day number.</param>
/// <param name="Min">The lowest unit price, coins.</param>
/// <param name="Median">The median unit price, coins.</param>
/// <param name="Max">The highest unit price, coins.</param>
/// <param name="Sales">How many sales the day had.</param>
public readonly record struct PriceDayInfo(int Day, long Min, long Median, long Max, int Sales);

/// <summary>The recent daily price history of one item (docs/adr/ADR-030 D4).</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Today">The game day number when read.</param>
/// <param name="Days">The days that had sales, oldest first.</param>
/// <param name="Average">The mean unit price of the last seven game days, coins; zero when nothing sold in them.</param>
public sealed record PriceHistoryInfo(int ItemId, int Today, IReadOnlyList<PriceDayInfo> Days, long Average)
{
    /// <summary>Gets the state before any read.</summary>
    public static readonly PriceHistoryInfo Empty = new(0, 0, [], 0);
}

/// <summary>Which request a result answers.</summary>
public enum MarketAction
{
    /// <summary>A listing was created.</summary>
    Create = 1,

    /// <summary>A listing was bought.</summary>
    Buy = 2,

    /// <summary>A listing was cancelled.</summary>
    Cancel = 3,

    /// <summary>Items were shipped at the bin.</summary>
    Ship = 4,
}

/// <summary>
/// The market's side of the connection: browses and reads listings, creates, buys and cancels them, and ships crops at the bin. The
/// server decides every outcome; the channel keeps the last page it read and reports each result.
/// </summary>
public sealed class MarketChannel
{
    private readonly IGameSocket _socket;

    /// <summary>Initializes the channel.</summary>
    /// <param name="socket">The game connection.</param>
    public MarketChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
    }

    /// <summary>Raised when the browse page changed. May run on any thread.</summary>
    public event Action? BrowseChanged;

    /// <summary>Raised when the player's own listings changed. May run on any thread.</summary>
    public event Action? MineChanged;

    /// <summary>Raised with the server's answer to a create, buy, cancel or ship; the number is the coins paid by a shipment. May run on any thread.</summary>
    public event Action<MarketAction, MarketStatus, long>? Completed;

    /// <summary>Raised when the price history changed. May run on any thread.</summary>
    public event Action? HistoryChanged;

    /// <summary>Gets the last price history read.</summary>
    public PriceHistoryInfo History { get; private set; } = PriceHistoryInfo.Empty;

    /// <summary>Gets the last browse page.</summary>
    public ListingsInfo Browsed { get; private set; } = ListingsInfo.Empty;

    /// <summary>Gets the player's own listings.</summary>
    public ListingsInfo Mine { get; private set; } = ListingsInfo.Empty;

    /// <summary>Reads a page of listings.</summary>
    /// <param name="page">Page number, zero based.</param>
    /// <param name="itemIds">Only these items; empty for all.</param>
    /// <param name="sort">The order of the page (a <see cref="MarketSort"/> value); newest first by default.</param>
    public void Browse(int page, IReadOnlyList<int> itemIds, MarketSort sort = MarketSort.Newest)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        _ = this.BrowseAsync(page, itemIds, (int)sort);
    }

    /// <summary>Reads the daily price history of one item.</summary>
    /// <param name="itemId">The item.</param>
    public void LoadHistory(int itemId) => _ = this.HistoryAsync(itemId);

    /// <summary>Reads the player's own listings.</summary>
    public void LoadMine() => _ = this.MineAsync();

    /// <summary>Lists a stack.</summary>
    /// <param name="slot">Inventory slot.</param>
    /// <param name="itemId">The item expected in the slot.</param>
    /// <param name="quantity">How many.</param>
    /// <param name="price">Price for the whole stack.</param>
    public void Create(int slot, int itemId, int quantity, long price) => _ = this.CreateAsync(slot, itemId, quantity, price);

    /// <summary>Buys a listing.</summary>
    /// <param name="listingId">The listing.</param>
    public void Buy(Guid listingId) => _ = this.ListingAsync(MarketAction.Buy, listingId);

    /// <summary>Cancels the player's own listing.</summary>
    /// <param name="listingId">The listing.</param>
    public void Cancel(Guid listingId) => _ = this.ListingAsync(MarketAction.Cancel, listingId);

    /// <summary>Ships items at the bin.</summary>
    /// <param name="slot">Inventory slot.</param>
    /// <param name="itemId">The item expected in the slot.</param>
    /// <param name="quantity">How many; zero ships the whole stack.</param>
    public void Ship(int slot, int itemId, int quantity) => _ = this.ShipAsync(slot, itemId, quantity);

    private static ListingsInfo Map(ListingsPage page)
    {
        List<ListingInfo> rows = new(page.Rows.Count);
        foreach (ListingRow row in page.Rows)
        {
            rows.Add(new ListingInfo(row.ListingId, row.ItemId, row.Quantity, row.Price, row.ExpiresAtMs, row.IsMine, row.SellerNickname));
        }

        return new ListingsInfo(page.ServerNowMs, page.Page, page.Total, rows);
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost read changes nothing; the player asks again.")]
    private async Task BrowseAsync(int page, IReadOnlyList<int> itemIds, int sortBy)
    {
        try
        {
            using PacketScope<BrowseListings> request = PacketFactory<BrowseListings>.Acquire();
            request.Value.Page = page;
            request.Value.SortBy = sortBy;
            request.Value.ItemIds.Clear();
            request.Value.ItemIds.AddRange(itemIds);
            ListingsPage reply = await _socket.RequestAsync<ListingsPage>(request.Value).ConfigureAwait(false);
            this.Browsed = Map(reply);
            this.BrowseChanged?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost read changes nothing; the player asks again.")]
    private async Task HistoryAsync(int itemId)
    {
        try
        {
            using PacketScope<GetPriceHistory> request = PacketFactory<GetPriceHistory>.Acquire();
            request.Value.ItemId = itemId;
            PriceHistory reply = await _socket.RequestAsync<PriceHistory>(request.Value).ConfigureAwait(false);
            List<PriceDayInfo> days = new(reply.Days.Count);
            foreach (PriceDayRow row in reply.Days)
            {
                days.Add(new PriceDayInfo(row.Day, row.Min, row.Median, row.Max, row.Sales));
            }

            this.History = new PriceHistoryInfo(reply.ItemId, reply.Today, days, reply.Average);
            this.HistoryChanged?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost read changes nothing; the player asks again.")]
    private async Task MineAsync()
    {
        try
        {
            using PacketScope<GetMyListings> request = PacketFactory<GetMyListings>.Acquire();
            ListingsPage reply = await _socket.RequestAsync<ListingsPage>(request.Value).ConfigureAwait(false);
            this.Mine = Map(reply);
            this.MineChanged?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task CreateAsync(int slot, int itemId, int quantity, long price)
    {
        try
        {
            using PacketScope<CreateListing> request = PacketFactory<CreateListing>.Acquire();
            request.Value.Slot = (byte)slot;
            request.Value.ItemId = itemId;
            request.Value.Quantity = quantity;
            request.Value.Price = price;
            MarketResult reply = await _socket.RequestAsync<MarketResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(MarketAction.Create, reply.Status, 0);
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task ListingAsync(MarketAction action, Guid listingId)
    {
        try
        {
            MarketResult reply;
            if (action == MarketAction.Buy)
            {
                using PacketScope<BuyListing> request = PacketFactory<BuyListing>.Acquire();
                request.Value.ListingId = listingId;
                reply = await _socket.RequestAsync<MarketResult>(request.Value).ConfigureAwait(false);
            }
            else
            {
                using PacketScope<CancelListing> request = PacketFactory<CancelListing>.Acquire();
                request.Value.ListingId = listingId;
                reply = await _socket.RequestAsync<MarketResult>(request.Value).ConfigureAwait(false);
            }

            this.Completed?.Invoke(action, reply.Status, 0);
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task ShipAsync(int slot, int itemId, int quantity)
    {
        try
        {
            using PacketScope<ShipItems> request = PacketFactory<ShipItems>.Acquire();
            request.Value.Slot = (byte)slot;
            request.Value.ItemId = itemId;
            request.Value.Quantity = quantity;
            ShipResult reply = await _socket.RequestAsync<ShipResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(MarketAction.Ship, reply.Status, reply.Earned);
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Market;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Shop;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Market;

/// <summary>The market panel's tabs.</summary>
public enum MarketTab
{
    /// <summary>Browse and buy.</summary>
    Browse = 0,

    /// <summary>List a stack.</summary>
    Sell = 1,

    /// <summary>The player's own listings.</summary>
    Mine = 2,
}

/// <summary>One listing row, already localized.</summary>
/// <param name="Id">The listing id.</param>
/// <param name="Item">The stack for the slot control.</param>
/// <param name="PriceText">The price, formatted.</param>
/// <param name="IsMine">Whether the viewer is the seller.</param>
/// <param name="SellerText">"Seller: nickname", already localized.</param>
public sealed record ListingDisplay(Guid Id, SlotDisplay Item, string PriceText, bool IsMine, string SellerText);

/// <summary>A listing the seller has been asked to confirm: a large stack (see <see cref="MarketViewModel.LargeListing"/>).</summary>
/// <param name="Slot">The inventory slot the stack is in.</param>
/// <param name="ItemId">The item.</param>
/// <param name="ItemName">The item's name, localized.</param>
/// <param name="Quantity">How many are listed.</param>
/// <param name="Price">The total price asked, in coins.</param>
public sealed record MarketListingRequest(int Slot, int ItemId, string ItemName, int Quantity, long Price);

/// <summary>
/// View model of the market panel: browse with a search box and pages, list a stack from the inventory with a price (showing the fee
/// the server will take), and the player's own listings. Every outcome is the server's; this class only asks and shows.
/// </summary>
public sealed class MarketViewModel : PanelViewModel, IDisposable
{
    /// <summary>From this many units, a listing waits for the seller's confirm: a big sale is easy to make by mistake.</summary>
    public const int LargeListing = 10;

    private readonly MarketChannel _market;
    private readonly InventoryViewModel _inventory;
    private readonly ItemDescriber _describer;
    private readonly IStringLocalizer<Catalogue> _catalogue;
    private readonly NicknameFormatter _names;
    private IReadOnlyList<int> _searchIds = [];
    private string _search = string.Empty;
    private int _sellSlot = -1;
    private MarketListingRequest? _pendingListing;
    private MarketListingRequest? _lastListed;
    private int _historyItemId;
    private MarketSort _sort = MarketSort.Newest;

    /// <summary>Initializes the view model.</summary>
    /// <param name="market">The market channel.</param>
    /// <param name="inventory">The inventory view model (the stacks that can be sold).</param>
    /// <param name="describer">Describes listed items.</param>
    /// <param name="catalogue">Item names, for the search box.</param>
    /// <param name="names">Shows the seller's nickname.</param>
    /// <param name="text">UI text.</param>
    public MarketViewModel(MarketChannel market, InventoryViewModel inventory, ItemDescriber describer, IStringLocalizer<Catalogue> catalogue, NicknameFormatter names, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(describer);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(names);

        _market = market;
        _inventory = inventory;
        _describer = describer;
        _catalogue = catalogue;
        _names = names;
        market.BrowseChanged += this.Raise;
        market.MineChanged += this.Raise;
        market.HistoryChanged += this.Raise;
        market.Completed += this.OnCompleted;
        inventory.Changed += this.Raise;
    }

    /// <summary>Gets the shown tab.</summary>
    public MarketTab Tab { get; private set; }

    /// <summary>Gets the search text.</summary>
    public string Search => _search;

    /// <summary>Gets the order of the browse page (newest first, or by unit price).</summary>
    public MarketSort Sort => _sort;

    /// <summary>Gets or sets the quantity typed in the sell form.</summary>
    public string SellQuantityText { get; set; } = "1";

    /// <summary>Gets or sets the price typed in the sell form.</summary>
    public string SellPriceText { get; set; } = string.Empty;

    /// <summary>Gets the zero-based browse page.</summary>
    public int Page => _market.Browsed.Page;

    /// <summary>Gets the number of browse pages (at least one).</summary>
    public int PageCount => Math.Max(1, (_market.Browsed.Total + CoinRules.ListingsPerPage - 1) / CoinRules.ListingsPerPage);

    /// <summary>Gets the listings of the browse page.</summary>
    public IReadOnlyList<ListingDisplay> Browsed => this.Describe(_market.Browsed);

    /// <summary>Gets the player's own listings.</summary>
    public IReadOnlyList<ListingDisplay> Mine => this.Describe(_market.Mine);

    /// <summary>Gets the inventory stacks, for picking what to sell.</summary>
    public IReadOnlyList<SlotDisplay> InventorySlots
    {
        get
        {
            List<SlotDisplay> slots = [];
            foreach (SlotDisplay slot in _inventory.Slots)
            {
                slots.Add(slot with { IsSelected = slot.Index == _sellSlot });
            }

            return slots;
        }
    }

    /// <summary>Gets the stack picked for sale, or <see langword="null"/>.</summary>
    public SlotDisplay? SellItem
    {
        get
        {
            foreach (SlotDisplay slot in _inventory.Slots)
            {
                if (slot.Index == _sellSlot && !slot.IsEmpty)
                {
                    return slot;
                }
            }

            return null;
        }
    }

    /// <summary>Gets the line that tells the seller the fee and what they will receive, or an empty string until a valid price is typed.</summary>
    public string FeeSummary
    {
        get
        {
            if (!long.TryParse(this.SellPriceText, NumberStyles.None, CultureInfo.InvariantCulture, out long price) || price < CoinRules.MinListingPrice || price > CoinRules.MaxListingPrice)
            {
                return string.Empty;
            }

            long fee = CoinRules.MarketFee(price);
            return this.Text("Market_FeeSummary", Number(fee), Number(price - fee), CoinRules.MarketFeePercent);
        }
    }

    /// <summary>Gets the item whose price history is shown, or zero for none.</summary>
    public int HistoryItemId => _historyItemId;

    /// <summary>Gets the name of the item whose price history is shown.</summary>
    public string HistoryTitle => _historyItemId == 0 ? string.Empty : this.Text("Market_History_Title", _describer.Describe(0, _historyItemId, 1).Name);

    /// <summary>Gets the median unit price of each game day that had sales, oldest first: the points of the sparkline.</summary>
    public IReadOnlyList<long> HistoryPoints
    {
        get
        {
            PriceHistoryInfo history = _market.History;
            if (history.ItemId != _historyItemId)
            {
                return [];
            }

            List<long> points = new(history.Days.Count);
            foreach (PriceDayInfo day in history.Days)
            {
                points.Add(day.Median);
            }

            return points;
        }
    }

    /// <summary>Gets the lines under the sparkline: the latest day's lowest, median and highest price, the seven-day average when there were sales in it, then the range of the whole period; or a note that nothing sold.</summary>
    public IReadOnlyList<string> HistoryLines
    {
        get
        {
            PriceHistoryInfo history = _market.History;
            if (_historyItemId == 0 || history.ItemId != _historyItemId)
            {
                return [];
            }

            if (history.Days.Count == 0)
            {
                return [this.Text("Market_History_None", PriceHistoryRules.HistoryDays)];
            }

            PriceDayInfo latest = history.Days[^1];
            long low = long.MaxValue;
            long high = 0;
            foreach (PriceDayInfo day in history.Days)
            {
                low = Math.Min(low, day.Min);
                high = Math.Max(high, day.Max);
            }

            List<string> lines = [this.Text("Market_History_Latest", Number(latest.Min), Number(latest.Median), Number(latest.Max), latest.Sales)];
            if (history.Average != 0)
            {
                lines.Add(this.Text("Market_History_Average", PriceHistoryRules.AverageDays, Number(history.Average)));
            }

            lines.Add(this.Text("Market_History_Range", PriceHistoryRules.HistoryDays, Number(low), Number(high)));
            return lines;
        }
    }

    /// <summary>Shows the price history of an item under the list.</summary>
    /// <param name="itemId">The item.</param>
    public void ShowHistory(int itemId)
    {
        _historyItemId = itemId;
        _market.LoadHistory(itemId);
        this.Raise();
    }

    /// <summary>Hides the price history.</summary>
    public void HideHistory()
    {
        _historyItemId = 0;
        this.Raise();
    }

    /// <summary>Called when the panel opens: loads the first page and the player's listings.</summary>
    public void Open()
    {
        _historyItemId = 0;
        this.ClearStatus();
        this.RunSearch(string.Empty);
        _market.LoadMine();
    }

    /// <summary>Switches tab.</summary>
    /// <param name="tab">The tab.</param>
    public void SetTab(MarketTab tab)
    {
        this.Tab = tab;
        _historyItemId = 0;
        this.ClearStatus();
        if (tab == MarketTab.Mine)
        {
            _market.LoadMine();
        }
        else if (tab == MarketTab.Browse)
        {
            this.GoToPage(0);
        }
    }

    /// <summary>Searches listings by item name (matched client side against the localized names; the server filters by item ids).</summary>
    /// <param name="text">The typed text; empty shows everything.</param>
    public void RunSearch(string text)
    {
        _search = text ?? string.Empty;
        List<int> ids = [];
        if (_search.Trim().Length > 0)
        {
            CompareInfo compare = CultureInfo.CurrentCulture.CompareInfo;
            foreach (ItemDefinition item in ItemCatalog.All)
            {
                if (item.IsTradable && compare.IndexOf(_catalogue["Item_" + item.Key + "_Name"].Value, _search.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
                {
                    ids.Add(item.Id);
                }
            }

            if (ids.Count == 0)
            {
                // Nothing is called that: ask for an item that no listing can hold.
                ids.Add(-1);
            }
        }

        _searchIds = ids;
        this.GoToPage(0);
    }

    /// <summary>Loads a browse page.</summary>
    /// <param name="page">Zero-based page.</param>
    public void GoToPage(int page) => _market.Browse(Math.Max(0, page), _searchIds, _sort);

    /// <summary>Gets the tabs that choose the order of the browse page: newest, lowest or highest unit price (docs/adr/ADR-013 amendment 2026-11-01).</summary>
    public IReadOnlyList<(int Id, string Label)> SortTabs =>
    [
        ((int)MarketSort.Newest, this.Text("Market_Sort_Newest")),
        ((int)MarketSort.UnitPriceLowest, this.Text("Market_Sort_UnitLowest")),
        ((int)MarketSort.UnitPriceHighest, this.Text("Market_Sort_UnitHighest")),
    ];

    /// <summary>Changes the order of the browse page and shows its first page in that order.</summary>
    /// <param name="sort">The order.</param>
    public void SetSort(MarketSort sort)
    {
        _sort = sort;
        this.GoToPage(0);
    }

    /// <summary>Chooses an order by the id of its tab (see <see cref="SortTabs"/>); an unknown id reads as newest first.</summary>
    /// <param name="tabId">The tab's id.</param>
    public void ChooseSort(int tabId) => this.SetSort(tabId is >= 0 and <= (int)MarketSort.UnitPriceHighest ? (MarketSort)tabId : MarketSort.Newest);

    /// <summary>
    /// Gets the line that tells the seller what the shipping bin pays for the picked stack, shown until a price is typed (then the fee line takes its place, so the
    /// sell form never needs a second line of text); an empty string when the bin does not buy it.
    /// </summary>
    public string BinHint
    {
        get
        {
            if (this.SellItem is not { } item || !ShippingPrices.TryGetSellPrice(item.ItemId, out int each) || this.FeeSummary.Length > 0)
            {
                return string.Empty;
            }

            int quantity = int.TryParse(this.SellQuantityText, NumberStyles.None, CultureInfo.InvariantCulture, out int typed) && typed > 0 ? typed : 1;
            return this.Text("Market_BinHint", Number(each), Number((long)each * quantity));
        }
    }

    /// <summary>Buys a listing.</summary>
    /// <param name="listingId">The listing.</param>
    public void Buy(Guid listingId) => _market.Buy(listingId);

    /// <summary>Withdraws one of the player's own listings.</summary>
    /// <param name="listingId">The listing.</param>
    public void Cancel(Guid listingId) => _market.Cancel(listingId);

    /// <summary>Gets the listing waiting for the seller's confirm, or <see langword="null"/> when none does.</summary>
    public MarketListingRequest? PendingListing => _pendingListing;

    /// <summary>Gets the question the confirm box asks: what, how many and the price.</summary>
    public string PendingListingText => _pendingListing is null
        ? string.Empty
        : this.Text("Market_Confirm_Text", _pendingListing.ItemName, _pendingListing.Quantity.ToString(CultureInfo.InvariantCulture), Number(_pendingListing.Price));

    /// <summary>Gets the preset the typed quantity matches (one, five or all of the stack), or -1 for none.</summary>
    public int ActiveSellPreset
    {
        get
        {
            int stack = this.SellItem?.Quantity ?? 0;
            int typed = int.TryParse(this.SellQuantityText, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : -1;
            return typed == ShopViewModel.PresetOne ? ShopViewModel.PresetOne
                : typed == ShopViewModel.PresetFive ? ShopViewModel.PresetFive
                : typed == stack && stack > 0 ? ShopViewModel.PresetAll
                : -1;
        }
    }

    /// <summary>The label of a quantity preset: "x5", or "All" for <see cref="ShopViewModel.PresetAll"/>.</summary>
    /// <param name="preset">A preset.</param>
    /// <returns>The button label, localized.</returns>
    public string PresetLabel(int preset) => preset == ShopViewModel.PresetAll ? this.Text("Shop_Preset_All") : this.Text("Shop_Preset_Count", preset.ToString(CultureInfo.InvariantCulture));

    /// <summary>Sets the quantity to list to a preset: one, five, or the whole stack. Never more than the stack holds.</summary>
    /// <param name="preset">A preset (<see cref="ShopViewModel.PresetOne"/>, <see cref="ShopViewModel.PresetFive"/> or <see cref="ShopViewModel.PresetAll"/>).</param>
    public void ChooseSellPreset(int preset)
    {
        int stack = this.SellItem?.Quantity ?? 0;
        int quantity = preset == ShopViewModel.PresetAll ? stack : Math.Min(preset, stack);
        this.SellQuantityText = Math.Max(1, quantity).ToString(CultureInfo.InvariantCulture);
        this.Raise();
    }

    /// <summary>Picks the inventory stack to sell.</summary>
    /// <param name="slot">The inventory slot.</param>
    public void PickSellSlot(int slot)
    {
        _sellSlot = slot;
        _pendingListing = null;
        if (this.SellItem is { } item)
        {
            this.SellQuantityText = item.Quantity.ToString(CultureInfo.InvariantCulture);
            this.ShowHistory(item.ItemId);
        }

        this.Raise();
    }

    /// <summary>Lists the picked stack at the typed price.</summary>
    public void Sell()
    {
        if (this.SellItem is not { } item)
        {
            this.SetStatus(this.Text("Market_Status_InvalidSlot"));
            return;
        }

        if (!int.TryParse(this.SellQuantityText, NumberStyles.None, CultureInfo.InvariantCulture, out int quantity))
        {
            this.SetStatus(this.Text("Market_Status_InvalidQuantity"));
            return;
        }

        if (!long.TryParse(this.SellPriceText, NumberStyles.None, CultureInfo.InvariantCulture, out long price))
        {
            this.SetStatus(this.Text("Market_Status_InvalidPrice"));
            return;
        }

        if (quantity >= LargeListing)
        {
            _pendingListing = new MarketListingRequest(item.Index, item.ItemId, item.Name, quantity, price);
            this.Raise();
            return;
        }

        _lastListed = new MarketListingRequest(item.Index, item.ItemId, item.Name, quantity, price);
        _market.Create(item.Index, item.ItemId, quantity, price);
    }

    /// <summary>Lists the stack the seller confirmed.</summary>
    public void ConfirmListing()
    {
        if (_pendingListing is not { } request)
        {
            return;
        }

        _pendingListing = null;
        this.ClearStatus();
        _lastListed = request;
        _market.Create(request.Slot, request.ItemId, request.Quantity, request.Price);
    }

    /// <summary>Drops the large listing waiting for confirmation; nothing is listed.</summary>
    public void CancelListing()
    {
        if (_pendingListing is null)
        {
            return;
        }

        _pendingListing = null;
        this.Raise();
    }

    /// <summary>Tells the panel the typed text changed so the fee line follows it.</summary>
    public void Touch() => this.Raise();

    /// <inheritdoc/>
    public void Dispose()
    {
        _market.BrowseChanged -= this.Raise;
        _market.MineChanged -= this.Raise;
        _market.HistoryChanged -= this.Raise;
        _market.Completed -= this.OnCompleted;
        _inventory.Changed -= this.Raise;
    }

    private List<ListingDisplay> Describe(ListingsInfo page)
    {
        List<ListingDisplay> rows = new(page.Listings.Count);
        foreach (ListingInfo listing in page.Listings)
        {
            string price = listing.Quantity > 1
                ? this.Text("Market_Price_Each", Number(listing.Price), Number(listing.Price / listing.Quantity))
                : this.Text("Market_Price", Number(listing.Price));
            rows.Add(new ListingDisplay(listing.Id, _describer.Describe(0, listing.ItemId, listing.Quantity), price, listing.IsMine, this.Text("Market_Seller", _names.Display(listing.SellerNickname))));
        }

        return rows;
    }

    private void OnCompleted(MarketAction action, MarketStatus status, long earned)
    {
        _ = earned;
        if (status == MarketStatus.Ok)
        {
            if (action == MarketAction.Create && _lastListed is { } listed)
            {
                // What was listed, and what the seller gets when it sells: the fee is the server's rule (CoinRules), shown, never decided here.
                this.SetStatus(this.Text("Market_Done_CreateDetail", listed.Quantity, listed.ItemName, Number(listed.Price), Number(CoinRules.SellerProceeds(listed.Price)), CoinRules.MarketFeePercent), good: true);
            }
            else
            {
                this.SetStatus(this.Text("Market_Done_" + action), good: true);
            }

            if (action == MarketAction.Create)
            {
                _sellSlot = -1;
                _historyItemId = 0;
                this.SellPriceText = string.Empty;
                _lastListed = null;
            }

            _market.LoadMine();
            _market.Browse(_market.Browsed.Page, _searchIds, _sort);
        }
        else
        {
            this.SetStatus(this.Text("Market_Status_" + status));
            _market.Browse(_market.Browsed.Page, _searchIds, _sort);
        }
    }
}

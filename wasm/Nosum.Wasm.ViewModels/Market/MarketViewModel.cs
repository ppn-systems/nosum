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

/// <summary>
/// View model of the market panel: browse with a search box and pages, list a stack from the inventory with a price (showing the fee
/// the server will take), and the player's own listings. Every outcome is the server's; this class only asks and shows.
/// </summary>
public sealed class MarketViewModel : PanelViewModel, IDisposable
{
    private readonly MarketChannel _market;
    private readonly InventoryViewModel _inventory;
    private readonly ItemDescriber _describer;
    private readonly IStringLocalizer<Catalogue> _catalogue;
    private readonly NicknameFormatter _names;
    private IReadOnlyList<int> _searchIds = [];
    private string _search = string.Empty;
    private int _sellSlot = -1;

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
        market.Completed += this.OnCompleted;
        inventory.Changed += this.Raise;
    }

    /// <summary>Gets the shown tab.</summary>
    public MarketTab Tab { get; private set; }

    /// <summary>Gets the search text.</summary>
    public string Search => _search;

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

    /// <summary>Called when the panel opens: loads the first page and the player's listings.</summary>
    public void Open()
    {
        this.ClearStatus();
        this.RunSearch(string.Empty);
        _market.LoadMine();
    }

    /// <summary>Switches tab.</summary>
    /// <param name="tab">The tab.</param>
    public void SetTab(MarketTab tab)
    {
        this.Tab = tab;
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
    public void GoToPage(int page) => _market.Browse(Math.Max(0, page), _searchIds);

    /// <summary>Buys a listing.</summary>
    /// <param name="listingId">The listing.</param>
    public void Buy(Guid listingId) => _market.Buy(listingId);

    /// <summary>Withdraws one of the player's own listings.</summary>
    /// <param name="listingId">The listing.</param>
    public void Cancel(Guid listingId) => _market.Cancel(listingId);

    /// <summary>Picks the inventory stack to sell.</summary>
    /// <param name="slot">The inventory slot.</param>
    public void PickSellSlot(int slot)
    {
        _sellSlot = slot;
        if (this.SellItem is { } item)
        {
            this.SellQuantityText = item.Quantity.ToString(CultureInfo.InvariantCulture);
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

        _market.Create(item.Index, item.ItemId, quantity, price);
    }

    /// <summary>Tells the panel the typed text changed so the fee line follows it.</summary>
    public void Touch() => this.Raise();

    /// <inheritdoc/>
    public void Dispose()
    {
        _market.BrowseChanged -= this.Raise;
        _market.MineChanged -= this.Raise;
        _market.Completed -= this.OnCompleted;
        _inventory.Changed -= this.Raise;
    }

    private List<ListingDisplay> Describe(ListingsInfo page)
    {
        List<ListingDisplay> rows = new(page.Listings.Count);
        foreach (ListingInfo listing in page.Listings)
        {
            rows.Add(new ListingDisplay(listing.Id, _describer.Describe(0, listing.ItemId, listing.Quantity), Number(listing.Price), listing.IsMine, this.Text("Market_Seller", _names.Display(listing.SellerNickname))));
        }

        return rows;
    }

    private void OnCompleted(MarketAction action, MarketStatus status, long earned)
    {
        _ = earned;
        if (status == MarketStatus.Ok)
        {
            this.SetStatus(this.Text("Market_Done_" + action));
            if (action == MarketAction.Create)
            {
                _sellSlot = -1;
                this.SellPriceText = string.Empty;
            }

            _market.LoadMine();
            _market.Browse(_market.Browsed.Page, _searchIds);
        }
        else
        {
            this.SetStatus(this.Text("Market_Status_" + status));
            _market.Browse(_market.Browsed.Page, _searchIds);
        }
    }
}

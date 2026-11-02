// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Shop;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;
using Nosum.Gameplay.Shops;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.Socket.Shop;
using Nosum.Wasm.ViewModels.Device;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Shop;

/// <summary>A purchase the player has been asked to confirm (issue #215): a bulk order or one that costs a quarter of the balance or more.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="ItemName">The item's name, localized.</param>
/// <param name="Quantity">How many.</param>
/// <param name="Total">What it costs, in coins.</param>
/// <param name="BalanceAfter">The coins left after it, as far as the client can tell (the server checks again).</param>
public sealed record ShopPurchase(int ItemId, string ItemName, int Quantity, long Total, long BalanceAfter);

/// <summary>Why the Buy button of a row is off for the typed quantity. The client predicts it; the server still decides.</summary>
public enum ShopBlock
{
    /// <summary>Nothing stops the purchase.</summary>
    None = 0,

    /// <summary>The balance does not cover the typed quantity.</summary>
    NotEnoughCoins = 1,

    /// <summary>The bag has no room for even one unit of the item.</summary>
    BagFull = 2,
}

/// <summary>One row of the shop, already localized.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Item">The stack for the slot control (one unit).</param>
/// <param name="PriceText">The price of one unit, formatted ("100 coins").</param>
/// <param name="CanBuy">Whether Buy is on for the typed quantity.</param>
/// <param name="Dimmed">Whether the balance does not cover one unit: the row is dimmed.</param>
/// <param name="BlockText">Why Buy is off, localized; empty when it is on.</param>
/// <param name="SeasonNote">Why the row is greyed out of season, for example "Plants in Spring"; empty when the item is in season. Such a row cannot be bought or selected.</param>
public sealed record ShopOfferDisplay(int ItemId, SlotDisplay Item, string PriceText, bool CanBuy, bool Dimmed, string BlockText, string SeasonNote = "");

/// <summary>The detail strip under the list: the selected item, what it does, and what the typed quantity costs.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Name">Its name, localized.</param>
/// <param name="Description">What it is, localized.</param>
/// <param name="Effects">What it does, one line each (food, walking speed, running).</param>
/// <param name="PriceText">The price of one unit.</param>
/// <param name="TotalText">What the typed quantity costs.</param>
/// <param name="AfterText">The coins left after it; empty when the balance does not cover it.</param>
/// <param name="BlockText">Why Buy is off, localized; empty when it is on.</param>
public sealed record ShopDetail(int ItemId, string Name, string Description, IReadOnlyList<string> Effects, string PriceText, string TotalText, string AfterText, string BlockText);

/// <summary>
/// View model of the shop panel (docs/adr/ADR-013 amendments). The catalogue and prices are the shared <see cref="ShopCatalog"/>; buying is an
/// intent the server prices and checks (where the player stands, coins, room). This class lists what the opened shop sells (each shop has its
/// own section and title, so there are no tabs), takes a quantity (x1, x5 or all the balance buys), shows the selected item's detail and the
/// outcome. One instance serves whichever shop is opened.
/// </summary>
public sealed class ShopViewModel : PanelViewModel, IDisposable
{
    private readonly ShopChannel _channel;
    private readonly WalletChannel _wallet;
    private readonly ItemDescriber _describer;
    private readonly CalendarChannel? _calendar;
    private readonly InventoryChannel? _inventory;
    private readonly IHaptics _haptics;
    private int _shopId;
    private int _selectedItemId;
    private ShopPurchase? _pending;

    /// <summary>The most a quantity box takes (its field is two digits).</summary>
    public const int MaxQuantity = 99;

    /// <summary>The preset that buys one unit.</summary>
    public const int PresetOne = 1;

    /// <summary>The preset that buys five units.</summary>
    public const int PresetFive = 5;

    /// <summary>The preset that buys as many units as the balance and the stack allow.</summary>
    public const int PresetAll = 0;

    /// <summary>The fewest rows a page of the list shows: three, so the list never scrolls in a 1920x1080 window with the detail strip.</summary>
    public const int MinRowsPerPage = 3;

    /// <summary>The most rows a page of the list shows, in one column (a phone). Two columns show at most <see cref="MaxItemsPerPage"/> / 2 rows.</summary>
    public const int MaxRowsPerPage = 8;

    /// <summary>The most items a page of the list shows (docs/design/ui-standard.md section 3): two columns of four rows fit a 1280 x 720 and a 1920 x 1080 window.</summary>
    public const int MaxItemsPerPage = 8;

    /// <summary>The most columns the list shows: two on a desktop, one on a phone (640 px and narrower).</summary>
    public const int MaxColumns = 2;

    private int _columns = MaxColumns;
    private int _rowsPerPage = MinRowsPerPage;

    /// <summary>Initializes the view model.</summary>
    /// <param name="channel">Sends purchases.</param>
    /// <param name="wallet">The coin balance.</param>
    /// <param name="describer">Describes items.</param>
    /// <param name="text">UI text.</param>
    /// <param name="calendar">Optional calendar channel for filtering seasonal seed offers.</param>
    /// <param name="haptics">Optional vibration feedback on a purchase's outcome; none when omitted.</param>
    /// <param name="inventory">Optional inventory: whether the bag has room. When it is not known, the bag is taken to have room.</param>
    public ShopViewModel(ShopChannel channel, WalletChannel wallet, ItemDescriber describer, IStringLocalizer<Language> text, CalendarChannel? calendar = null, IHaptics? haptics = null, InventoryChannel? inventory = null)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(describer);
        _channel = channel;
        _wallet = wallet;
        _describer = describer;
        _calendar = calendar;
        _inventory = inventory;
        _haptics = haptics ?? NoHaptics.Instance;
        channel.Completed += this.OnCompleted;
        wallet.Changed += this.Raise;
        if (calendar is not null)
        {
            calendar.Changed += this.Raise;
        }

        if (inventory is not null)
        {
            inventory.Changed += this.Raise;
        }
    }

    /// <summary>Gets the id of the opened shop, or zero before one was opened.</summary>
    public int ShopId => _shopId;

    /// <summary>Gets whether the currently opened shop is the carpenter shop.</summary>
    public bool IsCarpenterShop => _shopId == ShopCatalog.CarpenterShop;

    /// <summary>Gets whether the currently opened shop is the blacksmith shop.</summary>
    public bool IsBlacksmithShop => _shopId == ShopCatalog.Blacksmith;

    /// <summary>Gets or sets the quantity typed for the next purchase.</summary>
    public string QuantityText { get; set; } = "1";

    /// <summary>Gets the title of the opened shop ("Seed shop"), from the shop's key.</summary>
    public string Title => ShopCatalog.TryGet(_shopId, out ShopDefinition? shop) ? this.Text("Shop_Title_" + shop.Key) : string.Empty;

    /// <summary>Gets the purchase waiting for the player's confirm, or <see langword="null"/> when none does.</summary>
    public ShopPurchase? Pending => _pending;

    /// <summary>Gets the question the confirm box asks, localized: what, how many, the total and the coins left.</summary>
    public string PendingText => _pending is null
        ? string.Empty
        : this.Text("Shop_Confirm_Text", _pending.ItemName, _pending.Quantity.ToString(CultureInfo.InvariantCulture), Number(_pending.Total), Number(_pending.BalanceAfter));

    /// <summary>Gets the balance line.</summary>
    public string CoinsText => this.Text("Shop_Coins", Number(_wallet.Coins));

    /// <summary>Gets the zero-based page the list shows.</summary>
    public int Page { get; private set; }

    /// <summary>Gets the columns the list shows: 2 on a desktop, 1 on a phone.</summary>
    public int Columns => _columns;

    /// <summary>Gets the rows a page shows: whole rows that fit the window, clamped to <see cref="MinRowsPerPage"/> and <see cref="RowLimit"/>.</summary>
    public int RowsPerPage => _rowsPerPage;

    /// <summary>Gets the items a page shows: the columns times the rows.</summary>
    public int ItemsPerPage => _columns * _rowsPerPage;

    /// <summary>Gets the number of pages of the opened shop's offers (at least one).</summary>
    public int PageCount => PagesFor(this.Offers.Count, this.ItemsPerPage);

    /// <summary>Gets the offers on the current page.</summary>
    public List<ShopOfferDisplay> PageOffers
    {
        get
        {
            List<ShopOfferDisplay> all = this.Offers;
            int page = Math.Min(this.Page, this.PageCount - 1);
            return all.Skip(page * this.ItemsPerPage).Take(this.ItemsPerPage).ToList();
        }
    }

    /// <summary>Gets what the opened shop sells, in its display order, for the typed quantity.</summary>
    public List<ShopOfferDisplay> Offers
    {
        get
        {
            List<ShopOfferDisplay> rows = [];
            if (!ShopCatalog.TryGet(_shopId, out ShopDefinition? shop))
            {
                return rows;
            }

            GameCalendar? calendar = _calendar is { IsLoaded: true } ? new GameCalendar(_calendar.EpochMs, _calendar.DayLengthMs) : null;
            long nowMs = calendar is not null ? _calendar!.ServerNowMs() : 0;
            int quantity = this.Quantity;
            foreach (ShopOffer offer in shop.Offers)
            {
                // Out of season the row stays, greyed, and its price cell says why (docs/design/research-hud-and-loops.md, backlog item 4).
                if (calendar is not null && !ShopRules.IsInSeason(_shopId, offer.ItemId, calendar, nowMs))
                {
                    string reason = this.SeasonNoteOf(offer.ItemId);
                    // The price cell says only that the row is out of season; the seasons it grows in are in the row's tip (ShopPanel).
                    rows.Add(new ShopOfferDisplay(offer.ItemId, _describer.Describe(0, offer.ItemId, 1), this.Text("Shop_Row_OffSeason"), false, true, reason, reason));
                    continue;
                }

                ShopBlock block = this.BlockFor(offer.ItemId, quantity);
                rows.Add(new ShopOfferDisplay(
                    offer.ItemId,
                    _describer.Describe(0, offer.ItemId, 1),
                    this.Text("Shop_Price", Number(offer.CoinsEach)),
                    block == ShopBlock.None,
                    _wallet.Coins < offer.CoinsEach,
                    this.BlockText(block, offer.ItemId, quantity)));
            }

            return rows;
        }
    }

    /// <summary>Gets the row the detail strip shows: the selected one, or the first one the shop sells.</summary>
    public ShopOfferDisplay? SelectedOffer
    {
        get
        {
            List<ShopOfferDisplay> rows = this.Offers;
            return rows.FirstOrDefault(r => r.ItemId == _selectedItemId) ?? rows.FirstOrDefault();
        }
    }

    /// <summary>Gets the detail strip's content for the selected row, or <see langword="null"/> when the shop sells nothing.</summary>
    public ShopDetail? Detail
    {
        get
        {
            if (this.SelectedOffer is not { } offer)
            {
                return null;
            }

            long total = this.TotalFor(offer.ItemId, this.Quantity);
            bool covered = total <= _wallet.Coins;
            return new ShopDetail(
                offer.ItemId,
                offer.Item.Name,
                _describer.DescriptionOf(offer.ItemId),
                _describer.EffectsOf(offer.ItemId),
                offer.PriceText,
                this.Text("Shop_Detail_Total", Number(total), this.Quantity.ToString(CultureInfo.InvariantCulture)),
                covered ? this.Text("Shop_Detail_After", Number(_wallet.Coins - total)) : string.Empty,
                offer.BlockText);
        }
    }

    /// <summary>Gets the typed quantity, or 1 when it is not a positive whole number.</summary>
    public int Quantity => int.TryParse(this.QuantityText, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value > 0 ? value : 1;

    /// <summary>Gets the most units of the selected item the balance buys (at least one), the "all" preset.</summary>
    public int AllQuantity
    {
        get
        {
            if (this.SelectedOffer is not { } offer || !ItemCatalog.TryGet(offer.ItemId, out ItemDefinition? item) || !ShopCatalog.TryGet(_shopId, out ShopDefinition? shop))
            {
                return 1;
            }

            if (!shop.TryGetPrice(offer.ItemId, out int each))
            {
                return 1;
            }

            return Math.Max(1, ShopRules.MaxAffordable(_wallet.Coins, each, item.MaxStack, MaxQuantity));
        }
    }

    /// <summary>Gets the preset the typed quantity matches (<see cref="PresetOne"/>, <see cref="PresetFive"/> or <see cref="PresetAll"/>), or -1 for none.</summary>
    public int ActivePreset => this.Quantity == PresetOne ? PresetOne : this.Quantity == PresetFive ? PresetFive : this.Quantity == this.AllQuantity ? PresetAll : -1;

    /// <summary>The label of a quantity preset: "x5", or "All" for <see cref="PresetAll"/>.</summary>
    /// <param name="preset">A preset.</param>
    /// <returns>The button label, localized.</returns>
    public string PresetLabel(int preset) => preset == PresetAll ? this.Text("Shop_Preset_All") : this.Text("Shop_Preset_Count", preset.ToString(CultureInfo.InvariantCulture));

    /// <summary>Opens a shop (the server checks again that the player stands at it when they buy).</summary>
    /// <param name="shopId">The shop id from <see cref="ShopCatalog"/>.</param>
    public void Open(int shopId)
    {
        this.Page = 0;
        _shopId = shopId;
        _pending = null;
        _selectedItemId = 0;
        this.QuantityText = "1";
        this.ClearStatus();
        this.Raise();
    }

    /// <summary>Re-renders after the typed quantity changed (prices that can no longer be afforded dim).</summary>
    public void Touch() => this.Raise();

    /// <summary>
    /// Sets how many rows a page shows (the view measures the window and passes the whole rows that fit). The page changes so the row that was
    /// first on screen stays on screen, and the page is kept inside the pages there are.
    /// </summary>
    /// <param name="rows">The rows; clamped to <see cref="MinRowsPerPage"/>..<see cref="RowLimit"/> for the current columns.</param>
    public void SetRowsPerPage(int rows) => this.SetLayout(_columns, rows);

    /// <summary>
    /// Sets the page's shape from the window: the columns the list shows and the whole rows that fit. The first offer on screen stays on
    /// screen (its page is recomputed for the new size), and the page stays inside the pages there are.
    /// </summary>
    /// <param name="columns">The columns; clamped to 1..<see cref="MaxColumns"/>.</param>
    /// <param name="rows">The rows; clamped to <see cref="MinRowsPerPage"/>..<see cref="RowLimit"/> for those columns.</param>
    public void SetLayout(int columns, int rows)
    {
        int nextColumns = Math.Clamp(columns, 1, MaxColumns);
        int nextRows = Math.Clamp(rows, MinRowsPerPage, RowLimit(nextColumns));
        if (nextColumns == _columns && nextRows == _rowsPerPage)
        {
            return;
        }

        int firstOffer = this.Page * this.ItemsPerPage;
        _columns = nextColumns;
        _rowsPerPage = nextRows;
        this.Page = Math.Min(PageOf(firstOffer, this.ItemsPerPage), this.PageCount - 1);
        this.Raise();
    }

    /// <summary>The most rows a page shows with <paramref name="columns"/> columns: a page never holds more than <see cref="MaxItemsPerPage"/> items.</summary>
    /// <param name="columns">The columns; clamped to 1..<see cref="MaxColumns"/>.</param>
    /// <returns>The rows limit.</returns>
    public static int RowLimit(int columns) => Math.Max(MinRowsPerPage, Math.Min(MaxRowsPerPage, MaxItemsPerPage / Math.Clamp(columns, 1, MaxColumns)));

    /// <summary>The whole rows that fit a space: n rows take n × row + (n − 1) × gap. Clamped to <see cref="MinRowsPerPage"/>..<see cref="MaxRowsPerPage"/>.</summary>
    /// <param name="availablePx">The height the rows may take, in pixels (positive infinity when there is no limit).</param>
    /// <param name="rowPx">The height of one row, in pixels.</param>
    /// <param name="gapPx">The gap between two rows, in pixels.</param>
    /// <returns>The rows per page.</returns>
    public static int RowsThatFit(double availablePx, double rowPx, double gapPx)
    {
        if (!(rowPx > 0) || double.IsNaN(availablePx) || double.IsNaN(gapPx))
        {
            return MinRowsPerPage;
        }

        // The small tolerance absorbs layout rounding, so a row that fits exactly is not lost to a fraction of a pixel.
        double rows = Math.Floor(((availablePx + gapPx) / (rowPx + gapPx)) + 1e-6);
        return (int)Math.Clamp(double.IsNaN(rows) ? MinRowsPerPage : rows, MinRowsPerPage, MaxRowsPerPage);
    }

    /// <summary>The number of pages that hold <paramref name="offers"/> offers, <paramref name="rowsPerPage"/> to a page (at least one).</summary>
    /// <param name="offers">The offers.</param>
    /// <param name="rowsPerPage">The rows per page; at least one.</param>
    /// <returns>The page count.</returns>
    public static int PagesFor(int offers, int rowsPerPage) => Math.Max(1, (offers + rowsPerPage - 1) / Math.Max(1, rowsPerPage));

    /// <summary>The zero-based page that holds the offer at <paramref name="firstOffer"/> (zero-based) when a page shows <paramref name="rowsPerPage"/> rows.</summary>
    /// <param name="firstOffer">The index of the offer.</param>
    /// <param name="rowsPerPage">The rows per page; at least one.</param>
    /// <returns>The page.</returns>
    public static int PageOf(int firstOffer, int rowsPerPage) => firstOffer / Math.Max(1, rowsPerPage);

    /// <summary>Shows another page of the offers.</summary>
    /// <param name="page">The zero-based page; clamped to the pages there are.</param>
    public void GoToPage(int page)
    {
        int next = Math.Clamp(page, 0, this.PageCount - 1);
        if (next != this.Page)
        {
            this.Page = next;
            this.Raise();
        }
    }

    /// <summary>Makes a row the one the detail strip shows (the player tapped its item).</summary>
    /// <param name="itemId">The item of the row.</param>
    public void Select(int itemId)
    {
        if (_selectedItemId != itemId)
        {
            _selectedItemId = itemId;
            this.Raise();
        }
    }

    /// <summary>Sets the typed quantity to a preset: one, five, or all the balance allows.</summary>
    /// <param name="preset">A preset (<see cref="PresetOne"/>, <see cref="PresetFive"/> or <see cref="PresetAll"/>).</param>
    public void ChoosePreset(int preset)
    {
        int quantity = preset == PresetAll ? this.AllQuantity : Math.Clamp(preset, 1, MaxQuantity);
        this.QuantityText = quantity.ToString(CultureInfo.InvariantCulture);
        this.Raise();
    }

    /// <summary>Adds or takes away units in the quantity box, never below one or above <see cref="MaxQuantity"/>.</summary>
    /// <param name="delta">How many to add (negative to take away).</param>
    public void Step(int delta)
    {
        this.QuantityText = Math.Clamp(this.Quantity + delta, 1, MaxQuantity).ToString(CultureInfo.InvariantCulture);
        this.Raise();
    }

    /// <summary>Buys the typed quantity of an item. A bulk order, or one that costs a quarter of the balance or more, waits for <see cref="Confirm"/>.</summary>
    /// <param name="itemId">The item.</param>
    public void Buy(int itemId)
    {
        this.ClearStatus();

        // A seed out of season is greyed and cannot be bought; the server would refuse it too.
        if (this.Offers.Find(o => o.ItemId == itemId) is { SeasonNote.Length: > 0 })
        {
            return;
        }

        int quantity = this.Quantity;
        long total = this.TotalFor(itemId, quantity);
        if (total > 0 && (quantity > 1 || total * 4 >= _wallet.Coins))
        {
            _pending = new ShopPurchase(itemId, _describer.Describe(0, itemId, 1).Name, quantity, total, _wallet.Coins - total);
            this.Raise();
            return;
        }

        _channel.Buy(_shopId, itemId, quantity);
    }

    /// <summary>Sends the purchase the player confirmed.</summary>
    public void Confirm()
    {
        if (_pending is not { } purchase)
        {
            return;
        }

        _pending = null;
        this.ClearStatus();
        _channel.Buy(_shopId, purchase.ItemId, purchase.Quantity);
    }

    /// <summary>Drops the purchase waiting for confirmation; nothing is bought.</summary>
    public void CancelPurchase()
    {
        if (_pending is null)
        {
            return;
        }

        _pending = null;
        this.Raise();
    }

    /// <summary>Buys the typed quantity of an item from its offer display row.</summary>
    /// <param name="offer">The offer display row.</param>
    public void Buy(ShopOfferDisplay offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        this.Buy(offer.ItemId);
    }

    /// <summary>Buys the typed quantity of an item from its offer display row.</summary>
    /// <param name="offer">The offer display row.</param>
    /// <returns>A completed task.</returns>
    public Task BuyAsync(ShopOfferDisplay offer)
    {
        this.Buy(offer);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _channel.Completed -= this.OnCompleted;
        _wallet.Changed -= this.Raise;
        if (_calendar is not null)
        {
            _calendar.Changed -= this.Raise;
        }

        if (_inventory is not null)
        {
            _inventory.Changed -= this.Raise;
        }
    }

    // The first thing that stops a purchase of this quantity, as the client predicts it: the coins first, then the room in the bag.
    private ShopBlock BlockFor(int itemId, int quantity)
    {
        if (this.TotalFor(itemId, quantity) > _wallet.Coins)
        {
            return ShopBlock.NotEnoughCoins;
        }

        return this.HasRoomFor(itemId) ? ShopBlock.None : ShopBlock.BagFull;
    }

    // Why a row is greyed out of season: the seasons a seed grows in, or the season a seasonal piece of furniture is sold in. A counter's own
    // rotation has no season name to give, so it says only that the item is not sold now.
    private string SeasonNoteOf(int itemId)
    {
        if (PlaceableCatalog.TryGet(itemId, out PlaceableDefinition? piece) && piece.SoldIn is { } soldIn)
        {
            return this.Text("Shop_Block_SoldIn", this.Text("Season_" + soldIn));
        }

        if (ItemCatalog.TryGet(itemId, out ItemDefinition? item) && item.Kind == ItemKind.Seed && CropCatalog.TryGet(item.CropId, out CropDefinition? crop))
        {
            List<string> seasons = [];
            foreach (Season season in Enum.GetValues<Season>())
            {
                if (crop.Seasons.Contains(season))
                {
                    seasons.Add(this.Text("Season_" + season));
                }
            }

            return this.Text("Shop_Block_NotInSeason", string.Join(", ", seasons));
        }

        return this.Text("Shop_Block_NotThisSeason");
    }

    private string BlockText(ShopBlock block, int itemId, int quantity) => block switch
    {
        ShopBlock.NotEnoughCoins => this.Text("Shop_Block_NotEnoughCoins", Number(this.TotalFor(itemId, quantity) - _wallet.Coins)),
        ShopBlock.BagFull => this.Text("Shop_Block_BagFull"),
        _ => string.Empty,
    };

    // True when the bag holds a free slot, or a stack of the item with room in it. An unknown bag (not loaded yet) counts as room.
    private bool HasRoomFor(int itemId)
    {
        if (_inventory is null || _inventory.Capacity <= 0)
        {
            return true;
        }

        int max = ItemCatalog.TryGet(itemId, out ItemDefinition? item) ? item.MaxStack : 1;
        for (int slot = 0; slot < _inventory.Capacity; slot++)
        {
            SlotInfo info = _inventory.SlotAt(slot);
            if (info.ItemId == 0 || (info.ItemId == itemId && info.Quantity < max))
            {
                return true;
            }
        }

        return false;
    }

    private long TotalFor(int itemId, int quantity)
    {
        if (ShopCatalog.TryGet(_shopId, out ShopDefinition? shop))
        {
            foreach (ShopOffer offer in shop.Offers)
            {
                if (offer.ItemId == itemId)
                {
                    return (long)offer.CoinsEach * quantity;
                }
            }
        }

        return 0;
    }

    private void OnCompleted(ShopStatus status, long spent)
    {
        _ = spent;
        _haptics.Pulse(status == ShopStatus.Ok ? HapticKind.Success : HapticKind.Refusal);
        this.SetStatus(status == ShopStatus.Ok ? this.Text("Shop_Done") : this.Text("Shop_Status_" + status), status == ShopStatus.Ok);
    }
}

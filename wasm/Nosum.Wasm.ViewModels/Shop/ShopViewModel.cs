// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Shop;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Shops;
using Nosum.Wasm.Socket.Calendar;
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

/// <summary>One row of the shop, already localized.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Item">The stack for the slot control (one unit).</param>
/// <param name="PriceText">The price of one unit, formatted ("100 coins").</param>
/// <param name="CanAfford">Whether the balance covers the typed quantity.</param>
public sealed record ShopOfferDisplay(int ItemId, SlotDisplay Item, string PriceText, bool CanAfford);

/// <summary>
/// View model of the shop panel (docs/adr/ADR-013 amendments). The catalogue and prices are the shared <see cref="ShopCatalog"/>; buying is an
/// intent the server prices and checks (where the player stands, coins, room). This class lists what the opened shop sells (each shop has its
/// own section and title, so there are no tabs), takes a quantity and shows the outcome. One instance serves whichever shop is opened.
/// </summary>
public sealed class ShopViewModel : PanelViewModel, IDisposable
{
    private readonly ShopChannel _channel;
    private readonly WalletChannel _wallet;
    private readonly ItemDescriber _describer;
    private readonly CalendarChannel? _calendar;
    private readonly IHaptics _haptics;
    private int _shopId;
    private ShopPurchase? _pending;

    /// <summary>The most a quantity box takes (its field is two digits).</summary>
    public const int MaxQuantity = 99;

    /// <summary>Initializes the view model.</summary>
    /// <param name="channel">Sends purchases.</param>
    /// <param name="wallet">The coin balance.</param>
    /// <param name="describer">Describes items.</param>
    /// <param name="text">UI text.</param>
    /// <param name="calendar">Optional calendar channel for filtering seasonal seed offers.</param>
    /// <param name="haptics">Optional vibration feedback on a purchase's outcome; none when omitted.</param>
    public ShopViewModel(ShopChannel channel, WalletChannel wallet, ItemDescriber describer, IStringLocalizer<Language> text, CalendarChannel? calendar = null, IHaptics? haptics = null)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(describer);
        _channel = channel;
        _wallet = wallet;
        _describer = describer;
        _calendar = calendar;
        _haptics = haptics ?? NoHaptics.Instance;
        channel.Completed += this.OnCompleted;
        wallet.Changed += this.Raise;
        if (calendar is not null)
        {
            calendar.Changed += this.Raise;
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

    /// <summary>The offers on one page of the list (whole rows only, <see cref="OffersPerPage"/> to a page).</summary>
    public const int OffersPerPage = 5;

    /// <summary>Gets the zero-based page the list shows.</summary>
    public int Page { get; private set; }

    /// <summary>Gets the number of pages of the opened shop's offers (at least one).</summary>
    public int PageCount => Math.Max(1, (this.Offers.Count + OffersPerPage - 1) / OffersPerPage);

    /// <summary>Gets the offers on the current page.</summary>
    public List<ShopOfferDisplay> PageOffers
    {
        get
        {
            List<ShopOfferDisplay> all = this.Offers;
            int page = Math.Min(this.Page, this.PageCount - 1);
            return all.Skip(page * OffersPerPage).Take(OffersPerPage).ToList();
        }
    }

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

    /// <summary>Gets what the opened shop sells, in its display order.</summary>
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
                if (calendar is not null && !ShopRules.IsInSeason(offer.ItemId, calendar, nowMs))
                {
                    continue;
                }

                rows.Add(new ShopOfferDisplay(
                    offer.ItemId,
                    _describer.Describe(0, offer.ItemId, 1),
                    this.Text("Shop_Price", Number(offer.CoinsEach)),
                    _wallet.Coins >= (long)offer.CoinsEach * quantity));
            }

            return rows;
        }
    }

    /// <summary>Gets the typed quantity, or 1 when it is not a positive whole number.</summary>
    public int Quantity => int.TryParse(this.QuantityText, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value > 0 ? value : 1;

    /// <summary>Opens a shop (the server checks again that the player stands at it when they buy).</summary>
    /// <param name="shopId">The shop id from <see cref="ShopCatalog"/>.</param>
    public void Open(int shopId)
    {
        this.Page = 0;
        _shopId = shopId;
        _pending = null;
        this.QuantityText = "1";
        this.ClearStatus();
        this.Raise();
    }

    /// <summary>Re-renders after the typed quantity changed (prices that can no longer be afforded dim).</summary>
    public void Touch() => this.Raise();

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

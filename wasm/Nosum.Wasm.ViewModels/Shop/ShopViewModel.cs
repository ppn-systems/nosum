// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Shop;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Shops;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.Socket.Shop;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Shop;

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
    private int _shopId;

    /// <summary>Initializes the view model.</summary>
    /// <param name="channel">Sends purchases.</param>
    /// <param name="wallet">The coin balance.</param>
    /// <param name="describer">Describes items.</param>
    /// <param name="text">UI text.</param>
    /// <param name="calendar">Optional calendar channel for filtering seasonal seed offers.</param>
    public ShopViewModel(ShopChannel channel, WalletChannel wallet, ItemDescriber describer, IStringLocalizer<Language> text, CalendarChannel? calendar = null)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(describer);
        _channel = channel;
        _wallet = wallet;
        _describer = describer;
        _calendar = calendar;
        channel.Completed += this.OnCompleted;
        wallet.Changed += this.Raise;
        if (calendar is not null)
        {
            calendar.Changed += this.Raise;
        }
    }

    /// <summary>Gets the id of the opened shop, or zero before one was opened.</summary>
    public int ShopId => _shopId;

    /// <summary>Gets or sets the quantity typed for the next purchase.</summary>
    public string QuantityText { get; set; } = "1";

    /// <summary>Gets the title of the opened shop ("Seed shop"), from the shop's key.</summary>
    public string Title => ShopCatalog.TryGet(_shopId, out ShopDefinition? shop) ? this.Text("Shop_Title_" + shop.Key) : string.Empty;

    /// <summary>Gets the balance line.</summary>
    public string CoinsText => this.Text("Shop_Coins", Number(_wallet.Coins));

    /// <summary>Gets what the opened shop sells, in its display order.</summary>
    public IReadOnlyList<ShopOfferDisplay> Offers
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
        _shopId = shopId;
        this.QuantityText = "1";
        this.ClearStatus();
        this.Raise();
    }

    /// <summary>Re-renders after the typed quantity changed (prices that can no longer be afforded dim).</summary>
    public void Touch() => this.Raise();

    /// <summary>Buys the typed quantity of an item.</summary>
    /// <param name="itemId">The item.</param>
    public void Buy(int itemId)
    {
        this.ClearStatus();
        _channel.Buy(_shopId, itemId, this.Quantity);
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

    private void OnCompleted(ShopStatus status, long spent)
    {
        _ = spent;
        this.SetStatus(status == ShopStatus.Ok ? this.Text("Shop_Done") : this.Text("Shop_Status_" + status), status == ShopStatus.Ok);
    }
}

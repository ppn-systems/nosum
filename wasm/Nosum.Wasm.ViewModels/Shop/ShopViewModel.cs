// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Shop;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Shops;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.Socket.Shop;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Shop;

/// <summary>What a shop sells, grouped for its tabs.</summary>
public enum ShopTab
{
    /// <summary>Seeds.</summary>
    Seeds = 0,

    /// <summary>Furniture.</summary>
    Furniture = 1,

    /// <summary>Clothing.</summary>
    Clothing = 2,
}

/// <summary>One row of the shop, already localized.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Item">The stack for the slot control (one unit).</param>
/// <param name="PriceText">The price of one unit, formatted ("100 coins").</param>
/// <param name="CanAfford">Whether the balance covers the typed quantity.</param>
public sealed record ShopOfferDisplay(int ItemId, SlotDisplay Item, string PriceText, bool CanAfford);

/// <summary>
/// View model of the shop panel (docs/adr/ADR-013 amendments). The catalogue and prices are the shared <see cref="ShopCatalog"/>; buying is an
/// intent the server prices and checks (where the player stands, coins, room). This class lists, takes a quantity and shows the outcome.
/// </summary>
public sealed class ShopViewModel : PanelViewModel, IDisposable
{
    private readonly ShopChannel _channel;
    private readonly WalletChannel _wallet;
    private readonly ItemDescriber _describer;
    private int _shopId;

    /// <summary>Initializes the view model.</summary>
    /// <param name="channel">Sends purchases.</param>
    /// <param name="wallet">The coin balance.</param>
    /// <param name="describer">Describes items.</param>
    /// <param name="text">UI text.</param>
    public ShopViewModel(ShopChannel channel, WalletChannel wallet, ItemDescriber describer, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(describer);
        _channel = channel;
        _wallet = wallet;
        _describer = describer;
        channel.Completed += this.OnCompleted;
        wallet.Changed += this.Raise;
    }

    /// <summary>Gets the shown tab.</summary>
    public ShopTab Tab { get; private set; }

    /// <summary>Gets or sets the quantity typed for the next purchase.</summary>
    public string QuantityText { get; set; } = "1";

    /// <summary>Gets the title: "General store shop"-style, from the shop's key.</summary>
    public string Title => ShopCatalog.TryGet(_shopId, out ShopDefinition? shop) ? this.Text("Shop_Title", this.Text("Shop_Name_" + shop.Key)) : string.Empty;

    /// <summary>Gets the balance line.</summary>
    public string CoinsText => this.Text("Shop_Coins", Number(_wallet.Coins));

    /// <summary>Gets the offers of the shown tab.</summary>
    public IReadOnlyList<ShopOfferDisplay> Offers
    {
        get
        {
            List<ShopOfferDisplay> rows = [];
            if (!ShopCatalog.TryGet(_shopId, out ShopDefinition? shop))
            {
                return rows;
            }

            int quantity = this.Quantity;
            foreach (ShopOffer offer in shop.Offers)
            {
                if (!ItemCatalog.TryGet(offer.ItemId, out ItemDefinition? item) || TabOf(item) != this.Tab)
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
        this.Tab = ShopTab.Seeds;
        this.QuantityText = "1";
        this.ClearStatus();
        this.Raise();
    }

    /// <summary>Shows another tab.</summary>
    /// <param name="tab">The tab.</param>
    public void SetTab(ShopTab tab)
    {
        this.Tab = tab;
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
    }

    private static ShopTab TabOf(ItemDefinition item) => item.Kind switch
    {
        ItemKind.Furniture => ShopTab.Furniture,
        ItemKind.Clothing => ShopTab.Clothing,
        _ => ShopTab.Seeds,
    };

    private void OnCompleted(ShopStatus status, long spent)
    {
        _ = spent;
        this.SetStatus(status == ShopStatus.Ok ? this.Text("Shop_Done") : this.Text("Shop_Status_" + status));
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Market;
using Nosum.Contracts.Market.C2S;
using Nosum.Contracts.Market.S2C;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Market;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>
/// The market page's browse order, the unit price of a stack, the shipping bin's price for the picked stack and the line after a listing
/// (docs/adr/ADR-013 amendment 2026-11-01).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The packets and channels the view model reads from live as long as the test.")]
public sealed class MarketSortViewModelTests
{
    private const int ParsnipSlot = 3;

    [Fact]
    public void SetSort_AsksForTheOrderWithTheFirstPage()
    {
        using CultureScope scope = new("en", "en-US");
        (MarketViewModel vm, FakeGameSocket socket) = Create(new ListingsPage());

        vm.SetSort(MarketSort.UnitPriceLowest);

        BrowseListings request = socket.Requests.OfType<BrowseListings>().Last();
        request.Page.Should().Be(0);
        request.SortBy.Should().Be((int)MarketSort.UnitPriceLowest);
        vm.Sort.Should().Be(MarketSort.UnitPriceLowest);
    }

    [Fact]
    public void Browsed_ShowsTheTotalAndTheUnitPriceOfAStack()
    {
        using CultureScope scope = new("en", "en-US");
        ListingsPage page = new() { Total = 2 };
        page.Rows.Add(new ListingRow { ListingId = System.Guid.NewGuid(), ItemId = ItemCatalog.Parsnip, Quantity = 5, Price = 300, SellerNickname = "Mai" });
        page.Rows.Add(new ListingRow { ListingId = System.Guid.NewGuid(), ItemId = ItemCatalog.Parsnip, Quantity = 1, Price = 30, SellerNickname = "Lan" });
        (MarketViewModel vm, _) = Create(page);

        vm.Open();

        vm.Browsed.Select(l => l.PriceText).Should().Equal("300 coins (60 each)", "30 coins");
    }

    [Fact]
    public void BinHint_SaysWhatTheBinPaysForThePickedStack()
    {
        using CultureScope scope = new("en", "en-US");
        (MarketViewModel vm, _) = Create(new ListingsPage(), parsnips: 12);
        vm.PickSellSlot(ParsnipSlot);
        vm.SellQuantityText = "4";

        ShippingPrices.TryGetSellPrice(ItemCatalog.Parsnip, out int each).Should().BeTrue();
        vm.BinHint.Should().Be($"The shipping bin pays {each:N0} coins each, {(each * 4):N0} for this stack.");
    }

    [Fact]
    public void BinHint_GivesWayToTheFeeLineOnceAPriceIsTyped()
    {
        using CultureScope scope = new("en", "en-US");
        (MarketViewModel vm, _) = Create(new ListingsPage(), parsnips: 12);
        vm.PickSellSlot(ParsnipSlot);
        vm.SellQuantityText = "4";
        vm.BinHint.Should().NotBeEmpty();

        vm.SellPriceText = "120";

        vm.BinHint.Should().BeEmpty("one line under the form: the fee replaces the bin's price");
        vm.FeeSummary.Should().NotBeEmpty();
    }

    [Fact]
    public void BinHint_IsEmptyForAnItemTheBinDoesNotBuy()
    {
        using CultureScope scope = new("en", "en-US");
        (MarketViewModel vm, _) = Create(new ListingsPage(), parsnips: 12);
        vm.PickSellSlot(ParsnipSlot);

        // The seed slot of the stock is not a sold crop: nothing to compare with (the bin buys produce only).
        vm.PickSellSlot(2);

        vm.BinHint.Should().BeEmpty();
    }

    [Fact]
    public void Sell_ReportsWhatWasListedAndWhatTheSellerReceives()
    {
        using CultureScope scope = new("en", "en-US");
        (MarketViewModel vm, _) = Create(new ListingsPage(), parsnips: 12, listingResult: MarketStatus.Ok);
        vm.PickSellSlot(ParsnipSlot);
        vm.SellQuantityText = "4";
        vm.SellPriceText = "120";

        vm.Sell();

        // The fee is the server's rule (CoinRules): 5 % of 120, rounded up, so the seller receives 114.
        vm.Status.Should().Be($"Listed 4 × Parsnip for 120 coins. You receive {CoinRules.SellerProceeds(120)} coins when it sells, after the {CoinRules.MarketFeePercent}% fee.");
        vm.StatusIsGood.Should().BeTrue();
        CoinRules.SellerProceeds(120).Should().Be(114);
    }

    [Fact]
    public void Sell_WhenTheServerRefuses_ShowsTheReasonNotTheDetail()
    {
        using CultureScope scope = new("en", "en-US");
        (MarketViewModel vm, _) = Create(new ListingsPage(), parsnips: 12, listingResult: MarketStatus.NotAtCounter);
        vm.PickSellSlot(ParsnipSlot);
        vm.SellQuantityText = "4";
        vm.SellPriceText = "120";

        vm.Sell();

        vm.Status.Should().Be("Stand at the trader's counter in Town to trade.");
        vm.StatusIsGood.Should().BeFalse();
    }

    private static (MarketViewModel Vm, FakeGameSocket Socket) Create(ListingsPage page, int parsnips = 0, MarketStatus listingResult = MarketStatus.Ok)
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => page);
        socket.ReplyWith(_ => new MarketResult { Status = listingResult });
        socket.ReplyWith(_ => new PriceHistory { Today = 80 });
        socket.ReplyWith(_ =>
        {
            InventorySnapshot snapshot = new() { Capacity = 36, SelectedSlot = 0 };
            snapshot.Slots.Add(new InventorySlotRow { Slot = 2, ItemId = ItemCatalog.ParsnipSeeds, Quantity = 8 });
            if (parsnips > 0)
            {
                snapshot.Slots.Add(new InventorySlotRow { Slot = ParsnipSlot, ItemId = ItemCatalog.Parsnip, Quantity = parsnips });
            }

            return snapshot;
        });
        InventoryChannel inventoryChannel = new(socket);
        InventoryViewModel inventory = new(inventoryChannel, new FixedIcons(), Localizer<Catalogue>(), Localizer<Language>());
        inventoryChannel.Load();
        ItemDescriber describer = new(new FixedIcons(), Localizer<Catalogue>(), Localizer<Language>());
        NicknameFormatter names = new(Localizer<Language>(), new LookChannel(socket, () => 0));
        MarketViewModel vm = new(new MarketChannel(socket), inventory, describer, Localizer<Catalogue>(), names, Localizer<Language>());
        return (vm, socket);
    }

    private static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    private sealed class FixedIcons : IItemIconSource
    {
        public bool TryGet(int itemId, out ItemIcon icon)
        {
            icon = new ItemIcon("sheet.png", 0, 0, 16, 16, 64, 64);
            return true;
        }
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _ui = CultureInfo.CurrentUICulture;
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

        public CultureScope(string ui, string culture)
        {
            CultureInfo.CurrentUICulture = new CultureInfo(ui);
            CultureInfo.CurrentCulture = new CultureInfo(culture);
        }

        public void Dispose()
        {
            CultureInfo.CurrentUICulture = _ui;
            CultureInfo.CurrentCulture = _culture;
        }
    }
}

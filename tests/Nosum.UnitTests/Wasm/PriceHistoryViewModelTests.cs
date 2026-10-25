// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Market.C2S;
using Nosum.Contracts.Market.S2C;
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

/// <summary>The price history on the market page: what the view model asks for and how it shows the sales of the last days (docs/adr/ADR-030 D4).</summary>
public sealed class PriceHistoryViewModelTests
{
    [Fact]
    public void ShowHistory_AsksForThatItem_AndShowsTheDailyMediansAsTheLine()
    {
        using CultureScope scope = new("en", "en-US");
        (MarketViewModel vm, FakeGameSocket socket) = Create(
            new PriceDayRow { Day = 78, Min = 9, Median = 10, Max = 12, Sales = 3 },
            new PriceDayRow { Day = 79, Min = 11, Median = 14, Max = 20, Sales = 2 },
            new PriceDayRow { Day = 80, Min = 8, Median = 9, Max = 10, Sales = 4 });

        vm.ShowHistory(ItemCatalog.Parsnip);

        socket.Requests.OfType<GetPriceHistory>().Select(r => r.ItemId).Should().Equal(ItemCatalog.Parsnip);
        vm.HistoryItemId.Should().Be(ItemCatalog.Parsnip);
        vm.HistoryTitle.Should().Be("Price history: Parsnip");
        vm.HistoryPoints.Should().Equal(10, 14, 9);
        vm.HistoryLines.Should().Equal(
            "Latest day: lowest 8, median 9, highest 10 coins each; sales: 4",
            "Last 14 days: from 8 to 20 coins each");
    }

    [Fact]
    public void ShowHistory_AnItemThatNeverSold_SaysSo_AndHasNoLine()
    {
        using CultureScope scope = new("en", "en-US");
        (MarketViewModel vm, _) = Create();

        vm.ShowHistory(ItemCatalog.Radish);

        vm.HistoryPoints.Should().BeEmpty();
        vm.HistoryLines.Should().Equal("Nothing was sold in the last 14 days.");
    }

    [Fact]
    public void ShowHistory_InVietnamese_UsesTheVietnameseTexts()
    {
        using CultureScope scope = new("vi", "vi-VN");
        (MarketViewModel vm, _) = Create(new PriceDayRow { Day = 80, Min = 8, Median = 9, Max = 10, Sales = 4 });

        vm.ShowHistory(ItemCatalog.Parsnip);

        vm.HistoryTitle.Should().StartWith("Lịch sử giá: ");
        vm.HistoryLines[0].Should().Contain("thấp nhất 8").And.Contain("cao nhất 10");
    }

    [Fact]
    public void HideHistory_AndSwitchingTab_ClearTheItem()
    {
        (MarketViewModel vm, _) = Create(new PriceDayRow { Day = 80, Min = 8, Median = 9, Max = 10, Sales = 4 });
        vm.ShowHistory(ItemCatalog.Parsnip);

        vm.HideHistory();
        vm.HistoryItemId.Should().Be(0);
        vm.HistoryPoints.Should().BeEmpty();
        vm.HistoryLines.Should().BeEmpty();

        vm.ShowHistory(ItemCatalog.Parsnip);
        vm.SetTab(MarketTab.Sell);
        vm.HistoryItemId.Should().Be(0);
    }

    [Fact]
    public void PickSellSlot_ShowsThePriceHistoryOfThePickedItemSoTheSellerCanPriceIt()
    {
        (MarketViewModel vm, FakeGameSocket socket) = Create(new PriceDayRow { Day = 80, Min = 8, Median = 9, Max = 10, Sales = 4 });
        vm.SetTab(MarketTab.Sell);

        vm.PickSellSlot(2);

        socket.Requests.OfType<GetPriceHistory>().Select(r => r.ItemId).Should().Equal(ItemCatalog.ParsnipSeeds);
        vm.HistoryItemId.Should().Be(ItemCatalog.ParsnipSeeds);
    }

    [Fact]
    public void Status_TheListingLimitHasATextInBothLanguages()
    {
        using (CultureScope english = new("en", "en-US"))
        {
            (MarketViewModel vm, _) = Create();
            vm.Text("Market_Status_" + Nosum.Contracts.Market.MarketStatus.TooManyOfItem).Should().Be("You can have at most 500 units of one item listed at once.");
        }

        using CultureScope vietnamese = new("vi", "vi-VN");
        (MarketViewModel vi, _) = Create();
        vi.Text("Market_Status_" + Nosum.Contracts.Market.MarketStatus.TooManyOfItem).Should().Contain("500");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The channels live as long as the test.")]
    private static (MarketViewModel Vm, FakeGameSocket Socket) Create(params PriceDayRow[] days)
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(request =>
        {
            PriceHistory history = new() { ItemId = ((GetPriceHistory)request).ItemId, Today = 80 };
            history.Days.AddRange(days);
            return history;
        });
        socket.ReplyWith(_ =>
        {
            InventorySnapshot snapshot = new() { Capacity = 36, SelectedSlot = 0 };
            snapshot.Slots.Add(new InventorySlotRow { Slot = 2, ItemId = ItemCatalog.ParsnipSeeds, Quantity = 8 });
            return snapshot;
        });
        socket.ReplyWith(_ => new ListingsPage());
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
            icon = new ItemIcon("sheet.png", 0, 0, 16, 64, 64);
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

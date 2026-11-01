// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Market.S2C;
using Nosum.Contracts.Shop;
using Nosum.Contracts.Shop.C2S;
using Nosum.Contracts.Shop.S2C;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Shops;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.ViewModels.Device;
using Nosum.Wasm.Socket.Shop;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.ViewModels.Shop;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The shop panel's view model (docs/adr/ADR-013 amendments): one shop per id, its own title and list, the shop id in every purchase.</summary>
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the channels and wallet live as long as the test.")]
public sealed class ShopViewModelTests
{
    [Theory]
    [InlineData("en", ShopCatalog.SeedShop, "Seed shop")]
    [InlineData("en", ShopCatalog.ClothingShop, "Clothing shop")]
    [InlineData("en", ShopCatalog.FurnitureShop, "Furniture shop")]
    [InlineData("vi", ShopCatalog.SeedShop, "Cửa hàng hạt giống")]
    [InlineData("vi", ShopCatalog.ClothingShop, "Cửa hàng quần áo")]
    [InlineData("vi", ShopCatalog.FurnitureShop, "Cửa hàng nội thất")]
    public void Title_IsPerShopAndLocalized(string culture, int shopId, string expected)
    {
        using CultureScope scope = new(culture);
        (ShopViewModel vm, _) = Create();

        vm.Open(shopId);

        vm.ShopId.Should().Be(shopId);
        vm.Title.Should().Be(expected);
    }

    [Theory]
    [InlineData(ShopCatalog.SeedShop)]
    [InlineData(ShopCatalog.ClothingShop)]
    [InlineData(ShopCatalog.FurnitureShop)]
    public void Offers_AreExactlyTheCatalogueOfTheOpenedShop(int shopId)
    {
        (ShopViewModel vm, _) = Create();
        ShopCatalog.TryGet(shopId, out ShopDefinition? shop);

        vm.Open(shopId);

        vm.Offers.Select(o => o.ItemId).Should().Equal(shop!.Offers.Select(o => o.ItemId));
    }

    [Fact]
    public void Offers_TailorSellsTheRenameScrollWithItsName()
    {
        using CultureScope scope = new("en");
        (ShopViewModel vm, _) = Create();

        vm.Open(ShopCatalog.ClothingShop);

        vm.Offers.Should().ContainSingle(o => o.ItemId == ItemCatalog.RenameScroll)
            .Which.Item.Name.Should().Be("Rename Scroll");
    }

    [Fact]
    public void Open_AnotherShop_ReplacesTheListAndResetsQuantityAndStatus()
    {
        (ShopViewModel vm, _) = Create();
        vm.Open(ShopCatalog.SeedShop);
        vm.QuantityText = "5";

        vm.Open(ShopCatalog.ClothingShop);

        vm.QuantityText.Should().Be("1");
        vm.Status.Should().BeEmpty();
        vm.Offers.Select(o => o.ItemId).Should().OnlyContain(id => SoldByClothingShop(id));
    }

    [Fact]
    public void Title_BeforeAnyShopIsOpened_AndForAnUnknownShop_IsEmpty()
    {
        (ShopViewModel vm, _) = Create();
        vm.Title.Should().BeEmpty();
        vm.Offers.Should().BeEmpty();

        vm.Open(99);

        vm.Title.Should().BeEmpty();
        vm.Offers.Should().BeEmpty();
    }

    [Fact]
    public void Confirm_SendsTheOpenedShopsIdTheItemAndTheTypedQuantity_AndShowsTheOutcome()
    {
        using CultureScope scope = new("en");
        (ShopViewModel vm, List<(int Shop, int Item, int Quantity)> sent) = Create();
        vm.Open(ShopCatalog.FurnitureShop);
        vm.QuantityText = "3";

        vm.Buy(ItemCatalog.OakChair);
        vm.Confirm();

        sent.Should().Equal([(ShopCatalog.FurnitureShop, ItemCatalog.OakChair, 3)]);
        vm.Status.Should().Be("The shop does not sell that.", "the canned server reply is NotSold");
    }

    [Fact]
    public void Buy_SingleInexpensiveItem_SendsAtOnce()
    {
        using CultureScope scope = new("en");
        (ShopViewModel vm, List<(int Shop, int Item, int Quantity)> sent) = Create(10_000_000_000L);
        vm.Open(ShopCatalog.FurnitureShop);

        vm.Buy(ItemCatalog.OakChair);

        sent.Should().Equal([(ShopCatalog.FurnitureShop, ItemCatalog.OakChair, 1)]);
        vm.Pending.Should().BeNull();
    }

    [Fact]
    public void Buy_BulkOrder_WaitsForConfirmationAndSendsNothingYet()
    {
        using CultureScope scope = new("en");
        (ShopViewModel vm, List<(int Shop, int Item, int Quantity)> sent) = Create(10_000_000_000L);
        vm.Open(ShopCatalog.FurnitureShop);
        vm.QuantityText = "3";

        vm.Buy(ItemCatalog.OakChair);

        sent.Should().BeEmpty("no coins move before the player confirms");
        vm.Pending.Should().NotBeNull();
        vm.Pending!.Quantity.Should().Be(3);
        ShopCatalog.TryGet(ShopCatalog.FurnitureShop, out ShopDefinition? shop).Should().BeTrue();
        int each = shop!.Offers.Single(o => o.ItemId == ItemCatalog.OakChair).CoinsEach;
        vm.Pending.Total.Should().Be(3L * each, "the total is the price of three units");
        vm.Pending.BalanceAfter.Should().Be(10_000_000_000L - vm.Pending.Total);
        vm.PendingText.Should().Contain("3");
    }

    [Fact]
    public void Buy_CostingAQuarterOfTheBalance_AsksFirstEvenForOneItem()
    {
        using CultureScope scope = new("en");
        (ShopViewModel vm, List<(int Shop, int Item, int Quantity)> sent) = Create(1);
        vm.Open(ShopCatalog.FurnitureShop);

        vm.Buy(ItemCatalog.OakChair);

        sent.Should().BeEmpty();
        vm.Pending!.Quantity.Should().Be(1);
    }

    [Fact]
    public void CancelPurchase_DropsThePendingOrderAndSendsNothing()
    {
        using CultureScope scope = new("en");
        (ShopViewModel vm, List<(int Shop, int Item, int Quantity)> sent) = Create(10_000_000_000L);
        vm.Open(ShopCatalog.FurnitureShop);
        vm.QuantityText = "2";
        vm.Buy(ItemCatalog.OakChair);

        vm.CancelPurchase();
        vm.Confirm();

        vm.Pending.Should().BeNull();
        sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(5, 6)]
    [InlineData(99, 99)]
    [InlineData(0, 2)]
    public void Step_MovesTheQuantityOneAndStaysBetweenOneAndTheMaximum(int start, int expected)
    {
        (ShopViewModel vm, _) = Create();
        vm.QuantityText = start.ToString(System.Globalization.CultureInfo.InvariantCulture);

        vm.Step(1);

        vm.Quantity.Should().Be(expected);
    }

    [Fact]
    public void Step_BelowOne_KeepsOne()
    {
        (ShopViewModel vm, _) = Create();
        vm.QuantityText = "1";

        vm.Step(-1);

        vm.Quantity.Should().Be(1);
    }

    [Fact]
    public void Completion_VibratesALightSuccessForAPurchase_AndARefusalOtherwise()
    {
        using CultureScope scope = new("en");
        FakeGameSocket socket = new();
        RecordingHaptics haptics = new();
        socket.ReplyWith<ShopResult>(_ => new ShopResult { Status = ShopStatus.Ok, Spent = 10 });
        ShopViewModel vm = Vm(socket, haptics);
        vm.Open(ShopCatalog.FurnitureShop);

        vm.Buy(ItemCatalog.OakChair);
        socket.ReplyWith<ShopResult>(_ => new ShopResult { Status = ShopStatus.NotSold, Spent = 0 });
        vm.Buy(ItemCatalog.OakChair);

        haptics.Pulses.Should().Equal([HapticKind.Success, HapticKind.Refusal]);
    }

    private sealed class RecordingHaptics : IHaptics
    {
        public List<HapticKind> Pulses { get; } = [];

        public void Pulse(HapticKind kind) => this.Pulses.Add(kind);
    }

    [Fact]
    public void ChoosePreset_All_TypesTheMostTheBalanceAllowsWithinTheStack()
    {
        using CultureScope scope = new("en");
        ShopOffer first = ShopCatalog.TryGet(ShopCatalog.SeedShop, out ShopDefinition? seeds) ? seeds.Offers[0] : throw new System.InvalidOperationException("no seed offers");
        ItemCatalog.TryGet(first.ItemId, out ItemDefinition? item).Should().BeTrue();
        (ShopViewModel vm, _) = Create(first.CoinsEach * 7L + 1);
        vm.Open(ShopCatalog.SeedShop);

        vm.ChoosePreset(ShopViewModel.PresetAll);

        vm.Quantity.Should().Be(System.Math.Min(7, item!.MaxStack));
        vm.ActivePreset.Should().Be(ShopViewModel.PresetAll);
    }

    [Fact]
    public void ChoosePreset_FiveAndOne_SetTheQuantityAndTheActivePreset()
    {
        using CultureScope scope = new("en");
        (ShopViewModel vm, _) = Create(10_000_000_000L);
        vm.Open(ShopCatalog.SeedShop);

        vm.ChoosePreset(ShopViewModel.PresetFive);
        vm.Quantity.Should().Be(5);
        vm.ActivePreset.Should().Be(ShopViewModel.PresetFive);

        vm.ChoosePreset(ShopViewModel.PresetOne);
        vm.Quantity.Should().Be(1);
        vm.ActivePreset.Should().Be(ShopViewModel.PresetOne);
    }

    [Fact]
    public void Offers_DimAndSayWhyBuyIsOff_WhenTheBalanceCannotCoverOneUnit()
    {
        using CultureScope scope = new("en");
        (ShopViewModel vm, _) = Create(0);
        vm.Open(ShopCatalog.SeedShop);

        vm.Offers.Should().NotBeEmpty();
        vm.Offers.Should().OnlyContain(o => o.Dimmed && !o.CanBuy && o.BlockText.StartsWith("Not enough coins", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Offers_BuyIsOn_WhenTheBalanceCoversTheTypedQuantity()
    {
        using CultureScope scope = new("en");
        ShopOffer first = ShopCatalog.TryGet(ShopCatalog.SeedShop, out ShopDefinition? seeds) ? seeds.Offers[0] : throw new System.InvalidOperationException("no seed offers");
        (ShopViewModel vm, _) = Create(first.CoinsEach * 3L);
        vm.Open(ShopCatalog.SeedShop);
        vm.QuantityText = "3";

        ShopOfferDisplay row = vm.Offers.First(o => o.ItemId == first.ItemId);
        row.CanBuy.Should().BeTrue();
        row.Dimmed.Should().BeFalse();
        row.BlockText.Should().BeEmpty();

        vm.QuantityText = "4";
        vm.Offers.First(o => o.ItemId == first.ItemId).CanBuy.Should().BeFalse("four units cost more than the balance");
    }

    [Fact]
    public void Offers_SayTheBagIsFull_WhenNoSlotHoldsRoomForTheItem()
    {
        using CultureScope scope = new("en");
        ShopOffer first = ShopCatalog.TryGet(ShopCatalog.SeedShop, out ShopDefinition? seeds) ? seeds.Offers[0] : throw new System.InvalidOperationException("no seed offers");
        (ShopViewModel vm, _) = CreateWithBag(10_000_000_000L, ItemCatalog.OakChair, ItemCatalog.OakChair);
        vm.Open(ShopCatalog.SeedShop);

        ShopOfferDisplay row = vm.Offers.First(o => o.ItemId == first.ItemId);
        row.CanBuy.Should().BeFalse();
        row.BlockText.Should().Be("Your bag is full.");
    }

    [Fact]
    public void Detail_ShowsTheSelectedItemEffectAndWhatTheQuantityCosts()
    {
        using CultureScope scope = new("en");
        int price = ShopCatalog.TryGet(ShopCatalog.ClothingShop, out ShopDefinition? clothes) && clothes.TryGetPrice(ItemCatalog.LeatherBoots, out int each) ? each : throw new System.InvalidOperationException("boots not sold");
        (ShopViewModel vm, _) = Create(10_000L);
        vm.Open(ShopCatalog.ClothingShop);

        vm.Select(ItemCatalog.LeatherBoots);

        ShopDetail detail = vm.Detail ?? throw new System.InvalidOperationException("no detail");
        detail.ItemId.Should().Be(ItemCatalog.LeatherBoots);
        detail.Effects.Should().ContainSingle(e => e == "Walking speed: +5%");
        detail.TotalText.Should().Be($"Total: {price} coins for 1");
        detail.AfterText.Should().Be($"Coins after: {(10_000L - price):N0}");
        detail.BlockText.Should().BeEmpty();
    }

    [Fact]
    public void Detail_HasNoAfterText_WhenTheBalanceDoesNotCoverIt()
    {
        using CultureScope scope = new("en");
        (ShopViewModel vm, _) = Create(0);
        vm.Open(ShopCatalog.ClothingShop);
        vm.Select(ItemCatalog.LeatherBoots);

        vm.Detail!.AfterText.Should().BeEmpty();
        vm.Detail.BlockText.Should().StartWith("Not enough coins");
    }

    [Fact]
    public void RowsPerPage_BeforeAnyMeasure_IsTheThreeRowFloor()
    {
        (ShopViewModel vm, _) = Create();

        vm.RowsPerPage.Should().Be(ShopViewModel.MinRowsPerPage);
    }

    [Theory]
    [InlineData(20, 4)] // two columns (the default): at most four rows, so eight items
    [InlineData(1, ShopViewModel.MinRowsPerPage)]
    public void SetRowsPerPage_OutOfRange_ClampsToTheFloorAndTheMaximum(int rows, int expected)
    {
        (ShopViewModel vm, _) = Create();
        vm.Open(ShopCatalog.SeedShop);

        vm.SetRowsPerPage(rows);

        vm.RowsPerPage.Should().Be(expected);
    }

    [Fact]
    public void SetRowsPerPage_MoreRows_ShowsEightItemsOfTheOpenedShop()
    {
        (ShopViewModel vm, _) = Create();
        vm.Open(ShopCatalog.SeedShop);
        vm.Offers.Count.Should().BeGreaterThan(ShopViewModel.MaxRowsPerPage);

        vm.SetRowsPerPage(ShopViewModel.MaxRowsPerPage);

        vm.ItemsPerPage.Should().Be(ShopViewModel.MaxItemsPerPage);
        vm.PageOffers.Should().HaveCount(ShopViewModel.MaxItemsPerPage);
        vm.PageOffers.Select(o => o.ItemId).Should().Equal(vm.Offers.Take(ShopViewModel.MaxItemsPerPage).Select(o => o.ItemId));
    }

    [Fact]
    public void SetRowsPerPage_Grows_KeepsTheRowThatWasFirstOnScreen()
    {
        (ShopViewModel vm, _) = Create();
        vm.Open(ShopCatalog.SeedShop);
        vm.GoToPage(1);
        int firstShown = vm.Offers[vm.Page * vm.ItemsPerPage].ItemId;

        vm.SetRowsPerPage(ShopViewModel.MaxRowsPerPage);

        vm.PageOffers.Select(o => o.ItemId).Should().Contain(firstShown);
    }

    [Fact]
    public void SetRowsPerPage_ShrinksPastTheCurrentPage_KeepsThePageInRange()
    {
        (ShopViewModel vm, _) = Create();
        vm.Open(ShopCatalog.SeedShop);
        vm.SetRowsPerPage(ShopViewModel.MaxRowsPerPage);
        vm.GoToPage(vm.PageCount - 1);

        vm.SetRowsPerPage(ShopViewModel.MinRowsPerPage);

        vm.Page.Should().BeInRange(0, vm.PageCount - 1);
        vm.PageOffers.Should().NotBeEmpty();
        vm.PageOffers.Should().HaveCountLessThanOrEqualTo(vm.ItemsPerPage);
    }

    [Fact]
    public void SetLayout_OneColumn_ShowsRowsOfOneItemUpToEightRows()
    {
        (ShopViewModel vm, _) = Create();
        vm.Open(ShopCatalog.SeedShop);

        vm.SetLayout(1, ShopViewModel.MaxRowsPerPage);

        vm.Columns.Should().Be(1);
        vm.RowsPerPage.Should().Be(ShopViewModel.MaxRowsPerPage);
        vm.ItemsPerPage.Should().Be(ShopViewModel.MaxRowsPerPage);
    }

    [Fact]
    public void SetLayout_TwoColumns_CapsTheRowsSoThePageHoldsEightItems()
    {
        (ShopViewModel vm, _) = Create();
        vm.Open(ShopCatalog.SeedShop);

        vm.SetLayout(2, ShopViewModel.MaxRowsPerPage);

        vm.Columns.Should().Be(2);
        vm.RowsPerPage.Should().Be(ShopViewModel.RowLimit(2));
        vm.ItemsPerPage.Should().Be(ShopViewModel.MaxItemsPerPage);
    }

    [Fact]
    public void PageCount_WithSetRows_CountsWholeItemsOfThatSize()
    {
        (ShopViewModel vm, _) = Create();
        vm.Open(ShopCatalog.SeedShop);
        int offers = vm.Offers.Count;

        vm.SetRowsPerPage(ShopViewModel.MaxRowsPerPage);

        vm.PageCount.Should().Be(ShopViewModel.PagesFor(offers, ShopViewModel.MaxRowsPerPage));
    }

    private static (ShopViewModel Vm, List<(int Shop, int Item, int Quantity)> Sent) CreateWithBag(long coins, int firstSlotItem, int secondSlotItem)
    {
        FakeGameSocket socket = new();
        List<(int, int, int)> sent = [];
        socket.ReplyWith<ShopResult>(request =>
        {
            BuyFromShop buy = (BuyFromShop)request;
            sent.Add((buy.ShopId, buy.ItemId, buy.Quantity));
            return new ShopResult { Status = ShopStatus.NotSold, Spent = 0 };
        });
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        InventoryChannel bag = new(socket);
        socket.ReplyWith<InventorySnapshot>(_ => new InventorySnapshot
        {
            Capacity = 2,
            SelectedSlot = 0,
            Slots = [new InventorySlotRow { Slot = 0, ItemId = firstSlotItem, Quantity = 1 }, new InventorySlotRow { Slot = 1, ItemId = secondSlotItem, Quantity = 1 }],
        });
        bag.Load();
        ShopViewModel vm = new(new ShopChannel(socket), new WalletChannel(socket), new ItemDescriber(new NoIcons(), new StringLocalizer<Catalogue>(factory), new StringLocalizer<Language>(factory)), new StringLocalizer<Language>(factory), inventory: bag);
        socket.Push(new WalletUpdate { Coins = coins });
        return (vm, sent);
    }

    private static ShopViewModel Vm(FakeGameSocket socket, IHaptics haptics)
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        ShopViewModel vm = new(new ShopChannel(socket), new WalletChannel(socket), new ItemDescriber(new NoIcons(), new StringLocalizer<Catalogue>(factory), new StringLocalizer<Language>(factory)), new StringLocalizer<Language>(factory), haptics: haptics);
        socket.Push(new WalletUpdate { Coins = 10_000_000_000L });
        return vm;
    }

    private static bool SoldByClothingShop(int itemId) => ShopRules.Price(ShopCatalog.ClothingShop, itemId, 1, out _) == ShopVerdict.Ok;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The wallet channel lives as long as the test.")]
    private static (ShopViewModel Vm, List<(int Shop, int Item, int Quantity)> Sent) Create(long coins = 0)
    {
        FakeGameSocket socket = new();
        List<(int, int, int)> sent = [];
        socket.ReplyWith<ShopResult>(request =>
        {
            BuyFromShop buy = (BuyFromShop)request;
            sent.Add((buy.ShopId, buy.ItemId, buy.Quantity));
            return new ShopResult { Status = ShopStatus.NotSold, Spent = 0 };
        });
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        ShopViewModel vm = new(new ShopChannel(socket), new WalletChannel(socket), new ItemDescriber(new NoIcons(), new StringLocalizer<Catalogue>(factory), new StringLocalizer<Language>(factory)), new StringLocalizer<Language>(factory));
        socket.Push(new WalletUpdate { Coins = coins });
        return (vm, sent);
    }

    private sealed class NoIcons : IItemIconSource
    {
        public bool TryGet(int itemId, out ItemIcon icon)
        {
            icon = new ItemIcon("sheet.png", 0, 0, 16, 16, 64, 64);
            return true;
        }
    }

    private sealed class CultureScope : System.IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public CultureScope(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Contracts.Shop;
using Nosum.Contracts.Shop.C2S;
using Nosum.Contracts.Shop.S2C;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Shops;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.Socket.Shop;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.ViewModels.Shop;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The shop panel's view model (docs/adr/ADR-013 amendments): one shop per id, its own title and list, the shop id in every purchase.</summary>
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
    public void Buy_SendsTheOpenedShopsIdTheItemAndTheTypedQuantity_AndShowsTheOutcome()
    {
        using CultureScope scope = new("en");
        (ShopViewModel vm, List<(int Shop, int Item, int Quantity)> sent) = Create();
        vm.Open(ShopCatalog.FurnitureShop);
        vm.QuantityText = "3";

        vm.Buy(ItemCatalog.OakChair);

        sent.Should().Equal([(ShopCatalog.FurnitureShop, ItemCatalog.OakChair, 3)]);
        vm.Status.Should().Be("The shop does not sell that.", "the canned server reply is NotSold");
    }

    private static bool SoldByClothingShop(int itemId) => ShopRules.Price(ShopCatalog.ClothingShop, itemId, 1, out _) == ShopVerdict.Ok;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The wallet channel lives as long as the test.")]
    private static (ShopViewModel Vm, List<(int Shop, int Item, int Quantity)> Sent) Create()
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
        return (vm, sent);
    }

    private sealed class NoIcons : IItemIconSource
    {
        public bool TryGet(int itemId, out ItemIcon icon)
        {
            icon = new ItemIcon("sheet.png", 0, 0, 16, 64, 64);
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

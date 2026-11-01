// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Season.S2C;
using Nosum.Contracts.Shop;
using Nosum.Contracts.Shop.C2S;
using Nosum.Contracts.Shop.S2C;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Shops;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Contracts.Market.S2C;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.Socket.Shop;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.ViewModels.Shop;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>
/// Out-of-season seeds stay on the seed counter, greyed, with the reason in their price cell, and they cannot be bought (docs/design/research-hud-and-loops.md,
/// backlog item 4). The rule itself stays in <see cref="ShopRules.IsInSeason(int, int, GameCalendar, long)"/>.
/// </summary>
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the channels live as long as the test.")]
public sealed class ShopSeasonRowsTests
{
    [Fact]
    public void OutOfSeasonSeed_StaysListed_GreyedAndNotBuyable_WithTheReason()
    {
        using CultureScope scope = new("en");
        (ShopViewModel vm, List<(int, int, int)> sent, int itemId) = CreateInSpringWithAnOutOfSeasonSeed();

        vm.Open(ShopCatalog.SeedShop);
        ShopOfferDisplay row = vm.Offers.Should().ContainSingle(o => o.ItemId == itemId).Which;

        row.Dimmed.Should().BeTrue("the row is greyed");
        row.CanBuy.Should().BeFalse("it cannot be bought out of season");
        row.SeasonNote.Should().StartWith("Plants in ").And.NotBeEmpty();
        row.PriceText.Should().Be(row.SeasonNote, "the price cell says why");

        vm.Buy(itemId);
        sent.Should().BeEmpty("the server is not asked for an item that is out of season");
    }

    [Fact]
    public void InSeasonSeed_HasNoSeasonNote()
    {
        using CultureScope scope = new("en");
        (ShopViewModel vm, _, _) = CreateInSpringWithAnOutOfSeasonSeed();

        vm.Open(ShopCatalog.SeedShop);

        vm.Offers.Where(o => o.SeasonNote.Length == 0).Should().NotBeEmpty("some seeds grow in spring");
        vm.Offers.Where(o => o.SeasonNote.Length > 0).Should().NotBeEmpty("and some do not");
    }

    [Fact]
    public void Offers_KeepEveryCatalogueItemOfTheSeedCounter()
    {
        using CultureScope scope = new("en");
        (ShopViewModel vm, _, _) = CreateInSpringWithAnOutOfSeasonSeed();
        ShopCatalog.TryGet(ShopCatalog.SeedShop, out ShopDefinition? shop);

        vm.Open(ShopCatalog.SeedShop);

        vm.Offers.Select(o => o.ItemId).Should().Equal(shop!.Offers.Select(o => o.ItemId));
    }

    // A calendar in spring (the epoch's season), and the first seed on the seed counter that does not grow in spring.
    [SuppressMessage("Reliability", "CA2000", Justification = "The channels live as long as the test.")]
    private static (ShopViewModel Vm, List<(int Shop, int Item, int Quantity)> Sent, int ItemId) CreateInSpringWithAnOutOfSeasonSeed()
    {
        GameCalendar calendar = GameCalendar.Default;
        long serverNow = calendar.EpochMs + (calendar.DayLengthMs / 2);

        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new CalendarInfo { EpochMs = calendar.EpochMs, DayLengthMs = calendar.DayLengthMs, ServerNowMs = serverNow });
        CalendarChannel calendarChannel = new(socket);
        calendarChannel.Load();

        List<(int, int, int)> sent = [];
        socket.ReplyWith<ShopResult>(request =>
        {
            BuyFromShop buy = (BuyFromShop)request;
            sent.Add((buy.ShopId, buy.ItemId, buy.Quantity));
            return new ShopResult { Status = ShopStatus.Ok, Spent = 0 };
        });

        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        ShopViewModel vm = new(
            new ShopChannel(socket),
            new WalletChannel(socket),
            new ItemDescriber(new NoIcons(), new StringLocalizer<Catalogue>(factory), new StringLocalizer<Language>(factory)),
            new StringLocalizer<Language>(factory),
            calendarChannel);
        socket.Push(new WalletUpdate { Coins = 100_000 });

        ShopCatalog.TryGet(ShopCatalog.SeedShop, out ShopDefinition? shop);
        int itemId = shop!.Offers
            .Select(o => o.ItemId)
            .First(id => ItemCatalog.TryGet(id, out ItemDefinition? item) && item.Kind == ItemKind.Seed
                && CropCatalog.TryGet(item.CropId, out CropDefinition? crop) && !crop.GrowsIn(Season.Spring));
        return (vm, sent, itemId);
    }

    private sealed class NoIcons : Nosum.Wasm.ViewModels.Inventory.IItemIconSource
    {
        public bool TryGet(int itemId, out Nosum.Wasm.ViewModels.Inventory.ItemIcon icon)
        {
            icon = new Nosum.Wasm.ViewModels.Inventory.ItemIcon("sheet.png", 0, 0, 16, 16, 64, 64);
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

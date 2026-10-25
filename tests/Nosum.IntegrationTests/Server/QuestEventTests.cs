// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Quests;
using Nosum.Contracts.Market;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Quest;
using Nosum.Contracts.Shop;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Crafting;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Quests;
using Nosum.Gameplay.Shops;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Crafting;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Maps;
using Nosum.Infrastructure.Market;
using Nosum.Infrastructure.Shops;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The services that already decide an action tell the quests about it once it succeeded, and only then (docs/adr/ADR-036 D3): a purchase, a shipment and a craft
/// each report one event; a refusal reports nothing; a quest service that throws never turns the action into an error.
/// </summary>
public sealed class QuestEventTests : IClassFixture<PostgresFixture>
{
    private static readonly TmxMapCatalog s_maps = new(GameMaps.All, Path.Combine(AppContext.BaseDirectory, "maps"));

    private readonly PostgresFixture _database;

    public QuestEventTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Buy_SeedsAtTheSeedShop_ReportsThem_AndARefusedBuyReportsNothing()
    {
        using PlacementHarness world = new(_database);
        RecordingQuests quests = new();
        ShopService shop = new(world.Transfers, world.Presence, s_maps, GameCalendar.Default, TimeProvider.System, quests);
        Guid player = await world.CreatePlayerAsync();
        StandAtCounter(world, player, ShopCatalog.SeedShop);

        (await shop.BuyAsync(world.Presence.ConnectionOf(player), player, ShopCatalog.SeedShop, ItemCatalog.ParsnipSeeds, 3)).Status.Should().Be(ShopStatus.Ok);
        (await shop.BuyAsync(world.Presence.ConnectionOf(player), player, ShopCatalog.SeedShop, ItemCatalog.ParsnipSeeds, 100_000)).Status.Should().NotBe(ShopStatus.Ok);

        quests.Reports.Should().Equal((player, new QuestEvent(QuestEventKind.SeedBought, ItemCatalog.ParsnipSeeds, 3)));
    }

    [Fact]
    public async Task Buy_ThatIsNotSeeds_ReportsNothing()
    {
        using PlacementHarness world = new(_database);
        RecordingQuests quests = new();
        ShopService shop = new(world.Transfers, world.Presence, s_maps, GameCalendar.Default, TimeProvider.System, quests);
        Guid player = await world.CreatePlayerAsync();
        StandAtCounter(world, player, ShopCatalog.FurnitureShop);

        (await shop.BuyAsync(world.Presence.ConnectionOf(player), player, ShopCatalog.FurnitureShop, ItemCatalog.OakChair, 1)).Status.Should().Be(ShopStatus.Ok);

        quests.Reports.Should().BeEmpty("a chair is not seeds");
    }

    [Fact]
    public async Task Ship_AtTheBin_ReportsWhatWasShipped_AndAwayFromItReportsNothing()
    {
        using PlacementHarness world = new(_database);
        RecordingQuests quests = new();
        MarketService market = new(world.Transfers, world.Inventory, world.Units, world.Presence, s_maps, new CoinSink(), new NicknameDirectory(world.Units), world.Clock, quests: quests);
        Guid player = await world.CreatePlayerAsync();
        int slot = await world.GiveAsync(player, ItemCatalog.Parsnip, 4);
        Nosum.Core.PropPlacement bin = FarmProps.Placements.First(p => ReferenceEquals(p.Definition, FarmProps.ShippingBin));

        world.StandOnFarm(player, bin.TileX + 40, bin.TileY);
        (await market.ShipAsync(world.Presence.ConnectionOf(player), player, slot, ItemCatalog.Parsnip, 4)).Status.Should().Be(MarketStatus.NotAtBin);
        quests.Reports.Should().BeEmpty();

        world.StandOnFarm(player, bin.TileX, bin.TileY);
        (await market.ShipAsync(world.Presence.ConnectionOf(player), player, slot, ItemCatalog.Parsnip, 3)).Status.Should().Be(MarketStatus.Ok);

        quests.Reports.Should().Equal((player, new QuestEvent(QuestEventKind.ItemShipped, ItemCatalog.Parsnip, 3)));
    }

    [Fact]
    public async Task Craft_ReportsTheResult_AndALackOfWoodReportsNothing()
    {
        using PlacementHarness world = new(_database);
        RecordingQuests quests = new();
        CraftingService crafting = new(world.Presence, world.Inventory, new FixedSkillLevels(), quests);
        Guid player = await world.CreatePlayerAsync();
        world.StandOnFarm(player, 30, 30);

        (await crafting.CraftAsync(world.Presence.ConnectionOf(player), RecipeCatalog.Chest, 1)).Status.Should().Be(CraftStatus.NotEnough);
        quests.Reports.Should().BeEmpty();

        _ = await world.GiveAsync(player, ItemCatalog.Wood, 50);
        (await crafting.CraftAsync(world.Presence.ConnectionOf(player), RecipeCatalog.Chest, 1)).Status.Should().Be(CraftStatus.Ok);

        quests.Reports.Should().Equal((player, new QuestEvent(QuestEventKind.ItemCrafted, ItemCatalog.Chest, 1)));
    }

    [Fact]
    public async Task AQuestServiceThatThrows_DoesNotFailThePurchase()
    {
        using PlacementHarness world = new(_database);
        ShopService shop = new(world.Transfers, world.Presence, s_maps, GameCalendar.Default, TimeProvider.System, new RecordingQuests { Throw = true });
        Guid player = await world.CreatePlayerAsync();
        StandAtCounter(world, player, ShopCatalog.SeedShop);

        (await shop.BuyAsync(world.Presence.ConnectionOf(player), player, ShopCatalog.SeedShop, ItemCatalog.ParsnipSeeds, 1)).Status.Should().Be(ShopStatus.Ok);

        (await world.CountAsync(player, ItemCatalog.ParsnipSeeds)).Should().BeGreaterThanOrEqualTo(1);
    }

    private static void StandAtCounter(PlacementHarness world, Guid player, int shopId)
    {
        _ = GameMaps.TryGet(GameMaps.Town, out MapSpec? town);
        ShopPoint point = town!.Shops.Single(p => p.ShopId == shopId);
        world.Stand(player, MapInstance.Shared(GameMaps.Town), point.TileX, point.TileY);
    }

    private sealed class RecordingQuests : IQuestService
    {
        public List<(Guid Player, QuestEvent Event)> Reports { get; } = [];

        public bool Throw { get; init; }

        public event Func<QuestCompletion, ValueTask>? Completed
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<QuestView>> GetAsync(Guid playerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReportAsync(Guid playerId, QuestEvent questEvent, CancellationToken cancellationToken = default)
        {
            if (this.Throw)
            {
                throw new InvalidOperationException("boom");
            }

            this.Reports.Add((playerId, questEvent));
            return Task.CompletedTask;
        }
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;
using Nosum.Gameplay.Shops;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>
/// The furniture catalogue: every furniture item is a placeable in a catalogue group, the furnisher sells all of them, lamps are the only light
/// sources, pieces on the wall or the floor never block walking, and the seasonal pieces are sold only in their season.
/// </summary>
public sealed class FurnitureCatalogTests
{
    private static IEnumerable<PlaceableDefinition> Furniture() =>
        PlaceableCatalog.All.Where(piece => ItemCatalog.TryGet(piece.ItemId, out ItemDefinition? item) && item.Kind == ItemKind.Furniture);

    [Fact]
    public void EveryFurnitureItem_IsAPlaceable_WithACatalogueGroup()
    {
        List<ItemDefinition> items = ItemCatalog.All.Where(item => item.Kind == ItemKind.Furniture).ToList();

        items.Should().HaveCountGreaterThanOrEqualTo(39, "the seven first pieces plus the 32 added with the decorating update");
        foreach (ItemDefinition item in items)
        {
            PlaceableCatalog.TryGet(item.Id, out PlaceableDefinition? piece).Should().BeTrue(because: item.Key);
            piece!.Category.Should().NotBeNull(because: item.Key);
        }
    }

    [Fact]
    public void FurnitureShop_SellsEveryFurnitureItem()
    {
        ShopCatalog.TryGet(ShopCatalog.FurnitureShop, out ShopDefinition? shop).Should().BeTrue();
        IEnumerable<int> offered = shop!.Offers.Select(offer => offer.ItemId);

        foreach (ItemDefinition item in ItemCatalog.All.Where(item => item.Kind == ItemKind.Furniture))
        {
            offered.Should().Contain(item.Id, because: item.Key);
        }
    }

    [Fact]
    public void LightSources_AreExactlyTheLightingGroup()
    {
        foreach (PlaceableDefinition piece in Furniture())
        {
            (piece.LightRadius > 0).Should().Be(piece.Category == FurnitureCategory.Lighting, because: $"item {piece.ItemId}");
        }
    }

    [Theory]
    [InlineData(ItemCatalog.Candelabra)]
    [InlineData(ItemCatalog.TableLamp)]
    [InlineData(ItemCatalog.WallSconce)]
    [InlineData(ItemCatalog.PaperLantern)]
    [InlineData(ItemCatalog.FloorLamp)]
    public void Lamp_GivesLightAtNight(int itemId)
    {
        PlaceableCatalog.TryGet(itemId, out PlaceableDefinition? lamp).Should().BeTrue();

        lamp!.LightRadius.Should().BePositive();
    }

    [Theory]
    [InlineData(ItemCatalog.RedRug)]
    [InlineData(ItemCatalog.BlueRug)]
    [InlineData(ItemCatalog.RoundRug)]
    [InlineData(ItemCatalog.Doormat)]
    [InlineData(ItemCatalog.LandscapePainting)]
    [InlineData(ItemCatalog.PortraitPainting)]
    [InlineData(ItemCatalog.Window)]
    [InlineData(ItemCatalog.WallClock)]
    [InlineData(ItemCatalog.Mirror)]
    [InlineData(ItemCatalog.WallShelf)]
    [InlineData(ItemCatalog.WallSconce)]
    [InlineData(ItemCatalog.WinterWreath)]
    public void RugsAndWallPieces_DoNotBlockWalking(int itemId)
    {
        PlaceableCatalog.TryGet(itemId, out PlaceableDefinition? piece).Should().BeTrue();

        piece!.BlocksWalking.Should().BeFalse();
    }

    [Theory]
    [InlineData(ItemCatalog.OakChair)]
    [InlineData(ItemCatalog.SingleBed)]
    [InlineData(ItemCatalog.Wardrobe)]
    [InlineData(ItemCatalog.RoundTable)]
    public void StandingFurniture_BlocksWalking(int itemId)
    {
        PlaceableCatalog.TryGet(itemId, out PlaceableDefinition? piece).Should().BeTrue();

        piece!.BlocksWalking.Should().BeTrue();
    }

    [Theory]
    [InlineData(ItemCatalog.FlowerBox, Season.Spring)]
    [InlineData(ItemCatalog.PaperLantern, Season.Fall)]
    [InlineData(ItemCatalog.PumpkinDecor, Season.Fall)]
    [InlineData(ItemCatalog.WinterWreath, Season.Winter)]
    public void SeasonalPiece_IsSoldOnlyInItsSeason(int itemId, Season season)
    {
        GameCalendar calendar = GameCalendar.Default;

        ShopRules.IsInSeason(itemId, calendar, TimeInSeason(calendar, season)).Should().BeTrue();
        foreach (Season other in new[] { Season.Spring, Season.Summer, Season.Fall, Season.Winter }.Where(s => s != season))
        {
            ShopRules.IsInSeason(itemId, calendar, TimeInSeason(calendar, other)).Should().BeFalse(because: $"sold in {season} only");
        }
    }

    [Fact]
    public void YearRoundFurniture_IsSoldInEverySeason()
    {
        GameCalendar calendar = GameCalendar.Default;

        foreach (PlaceableDefinition piece in Furniture().Where(p => p.SoldIn is null))
        {
            ShopRules.IsInSeason(piece.ItemId, calendar, TimeInSeason(calendar, Season.Summer)).Should().BeTrue(because: $"item {piece.ItemId}");
        }
    }

    [Fact]
    public void SeasonalFurniture_IsExactlyTheFourRecordedSeasonalPieces()
    {
        Furniture().Where(p => p.SoldIn is not null).Select(p => p.ItemId).Should().BeEquivalentTo(
            new[] { ItemCatalog.FlowerBox, ItemCatalog.PaperLantern, ItemCatalog.PumpkinDecor, ItemCatalog.WinterWreath });
    }

    /// <summary>Returns a moment that falls in the given season of the default calendar.</summary>
    private static long TimeInSeason(GameCalendar calendar, Season season)
    {
        for (int day = 0; day < GameCalendar.DaysPerSeason * GameCalendar.SeasonsPerYear; day++)
        {
            long moment = calendar.EpochMs + day * calendar.DayLengthMs;
            if (calendar.SeasonAt(moment) == season)
            {
                return moment;
            }
        }

        throw new System.InvalidOperationException($"The calendar never reaches {season}.");
    }
}

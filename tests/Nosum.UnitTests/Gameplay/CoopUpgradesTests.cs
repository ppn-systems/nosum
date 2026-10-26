// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.UnitTests.Maps;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The pure rules of co-op upgrades (docs/adr/ADR-038): the price table, warehouse capacity and the unlocked rectangles of the co-op land.</summary>
public sealed class CoopUpgradesTests
{
    private static readonly CoopTrack[] s_tracks = [CoopTrack.Warehouse, CoopTrack.Land];

    [Theory]
    [InlineData(CoopTrack.Warehouse)]
    [InlineData(CoopTrack.Land)]
    public void Cost_EachBuyableLevel_HasAPriceOfRealTradableItems_ThatGrowsWithTheLevel(CoopTrack track)
    {
        int previousWood = 0;
        for (int level = 1; level <= CoopUpgrades.MaxLevel; level++)
        {
            var cost = CoopUpgrades.Cost(track, level);

            cost.Should().NotBeEmpty();
            cost.Should().OnlyContain(c => c.Quantity > 0);
            cost.Select(c => c.ItemId).Should().OnlyHaveUniqueItems();
            foreach (UpgradeCost line in cost)
            {
                ItemCatalog.TryGet(line.ItemId, out ItemDefinition? item).Should().BeTrue("item " + line.ItemId + " is in the catalogue");
                item!.IsTradable.Should().BeTrue("the price is taken from the warehouse, which only holds tradable items");
            }

            int wood = cost.Single(c => c.ItemId == ItemCatalog.Wood).Quantity;
            wood.Should().BeGreaterThan(previousWood);
            previousWood = wood;
        }
    }

    [Theory]
    [InlineData(CoopTrack.Warehouse, 0)]
    [InlineData(CoopTrack.Warehouse, CoopUpgrades.MaxLevel + 1)]
    [InlineData(CoopTrack.Land, -1)]
    [InlineData((CoopTrack)0, 1)]
    public void Cost_ALevelThatCannotBeBought_IsEmpty(CoopTrack track, int level)
        => CoopUpgrades.Cost(track, level).Should().BeEmpty();

    [Fact]
    public void WarehouseSlots_GrowWithEachLevel_AndClampOutsideTheRange()
    {
        int[] slots = [.. Enumerable.Range(0, CoopUpgrades.MaxLevel + 1).Select(CoopUpgrades.WarehouseSlots)];

        slots[0].Should().Be(CoopUpgrades.BaseSlots);
        slots.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        CoopUpgrades.WarehouseSlots(-5).Should().Be(slots[0]);
        CoopUpgrades.WarehouseSlots(99).Should().Be(slots[^1]);
    }

    [Theory]
    [InlineData(0, 19, false, true)]
    [InlineData(0, 20, false, false)]
    [InlineData(0, 20, true, true)]
    [InlineData(0, 25, true, true)]
    [InlineData(0, 25, false, false)]
    [InlineData(1, 20, false, true)]
    public void CanStore_ANewKindNeedsAFreeSlot_AKindAlreadyHeldAlwaysFits(int level, int kindsHeld, bool alreadyHeld, bool expected)
        => CoopUpgrades.CanStore(level, kindsHeld, alreadyHeld).Should().Be(expected);

    [Fact]
    public void CanBuy_WithEnoughStock_AtTheNextLevel_IsOk()
        => CoopUpgrades.CanBuy(CoopTrack.Land, 0, 1, item => 10_000).Should().Be(UpgradeVerdict.Ok);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void CanBuy_ALevelThatIsNotTheNextOne_IsRefused_SoARepeatedRequestPaysOnce(int current, int target)
        => CoopUpgrades.CanBuy(CoopTrack.Warehouse, current, target, item => 10_000).Should().Be(UpgradeVerdict.NotNextLevel);

    [Fact]
    public void CanBuy_AtTheTopLevel_SaysMaxLevel()
        => CoopUpgrades.CanBuy(CoopTrack.Land, CoopUpgrades.MaxLevel, CoopUpgrades.MaxLevel + 1, item => 10_000).Should().Be(UpgradeVerdict.MaxLevel);

    [Fact]
    public void CanBuy_UnknownTrack_IsRefused()
        => CoopUpgrades.CanBuy((CoopTrack)9, 0, 1, item => 10_000).Should().Be(UpgradeVerdict.UnknownTrack);

    [Theory]
    [InlineData(CoopTrack.Warehouse)]
    [InlineData(CoopTrack.Land)]
    public void CanBuy_OneItemShort_IsNotEnoughStock_ForEveryItemOfEveryLevel(CoopTrack track)
    {
        for (int level = 1; level <= CoopUpgrades.MaxLevel; level++)
        {
            foreach (UpgradeCost shortItem in CoopUpgrades.Cost(track, level))
            {
                CoopUpgrades.CanBuy(track, level - 1, level, item => item == shortItem.ItemId ? shortItem.Quantity - 1 : 10_000)
                    .Should().Be(UpgradeVerdict.NotEnoughStock, "level " + level + " lacks one " + shortItem.ItemId);
            }

            CoopUpgrades.CanBuy(track, level - 1, level, item => CoopUpgrades.Cost(track, level).Single(c => c.ItemId == item).Quantity)
                .Should().Be(UpgradeVerdict.Ok, "exactly the price is enough");
        }
    }

    [Fact]
    public void ReasonOf_NamesTheTrack_AndIsNotAWarehouseMove()
    {
        CoopUpgrades.ReasonOf(CoopTrack.Warehouse).Should().Be("coop.upgrade.warehouse");
        CoopUpgrades.ReasonOf(CoopTrack.Land).Should().Be("coop.upgrade.land");
        s_tracks.Select(CoopUpgrades.ReasonOf).Should().OnlyContain(r => !CoopLand.IsWarehouseReason(r), "an upgrade never changes what a member may withdraw");
    }

    [Fact]
    public void Land_TheArrivalAndTheExitTiles_AreOpenAtLevelZero()
    {
        CoopUpgrades.IsLandUnlocked(0, CoopLand.LandArrival.X, CoopLand.LandArrival.Y).Should().BeTrue();
        CoopUpgrades.IsLandUnlocked(0, CoopLand.LandGate.X, CoopLand.LandGate.Y).Should().BeTrue();
    }

    [Fact]
    public void Land_EachLevelOnlyAddsGround_AndTheTopLevelIsTheWholeMap()
    {
        for (int y = 0; y < 65; y++)
        {
            for (int x = 0; x < 80; x++)
            {
                for (int level = 0; level < CoopUpgrades.MaxLevel; level++)
                {
                    if (CoopUpgrades.IsLandUnlocked(level, x, y))
                    {
                        CoopUpgrades.IsLandUnlocked(level + 1, x, y).Should().BeTrue("a level never takes ground away: " + x + "," + y);
                    }
                }

                CoopUpgrades.IsLandUnlocked(CoopUpgrades.MaxLevel, x, y).Should().BeTrue();
            }
        }
    }

    [Theory]
    [InlineData(25, 45, 0, false)]
    [InlineData(25, 45, 1, true)]
    [InlineData(65, 30, 1, false)]
    [InlineData(65, 30, 2, true)]
    [InlineData(5, 2, 2, false)]
    [InlineData(5, 2, 3, true)]
    [InlineData(25, 24, 0, true)]
    [InlineData(-1, 24, 3, false)]
    [InlineData(25, 65, 3, false)]
    public void IsLandUnlocked_ChecksTheBlocksUpToTheLevel(int x, int y, int level, bool expected)
        => CoopUpgrades.IsLandUnlocked(level, x, y).Should().Be(expected);

    [Fact]
    public void IsAreaUnlocked_AFootprintAcrossTheEdge_NeedsEveryTile()
    {
        LandRect first = CoopUpgrades.LandStep(0);
        int lastRow = first.Y + first.Height - 1;

        CoopUpgrades.IsAreaUnlocked(0, 30, lastRow - 1, 2, 2).Should().BeTrue();
        CoopUpgrades.IsAreaUnlocked(0, 30, lastRow, 2, 2).Should().BeFalse("its second row is on locked ground");
        CoopUpgrades.IsAreaUnlocked(1, 30, lastRow, 2, 2).Should().BeTrue();
    }

    [Fact]
    public void LandSteps_FitTheMapOfTheCoopLand()
    {
        MapSpec spec = Spec();
        TileMap map = Load(spec);

        for (int level = 0; level <= CoopUpgrades.MaxLevel; level++)
        {
            LandRect step = CoopUpgrades.LandStep(level);
            (step.X >= 0 && step.Y >= 0 && step.X + step.Width <= map.Width && step.Y + step.Height <= map.Height).Should().BeTrue("level " + level);
        }

        CoopUpgrades.LandStep(CoopUpgrades.MaxLevel).Should().Be(new LandRect(0, 0, map.Width, map.Height));
    }

    [Fact]
    public void Land_OnTheRealMap_EachLevelUnlocksMoreTillableGround_AndLevelZeroIsEnoughToStart()
    {
        MapSpec spec = Spec();
        TileMap map = Load(spec);
        TillGrid till = TillGrid.FromMap(map, spec.BuildWalkGrid(map));

        int[] open = new int[CoopUpgrades.MaxLevel + 1];
        for (int level = 0; level < open.Length; level++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    if (till.IsTillable(x, y) && CoopUpgrades.IsLandUnlocked(level, x, y))
                    {
                        open[level]++;
                    }
                }
            }
        }

        open[0].Should().BeGreaterThan(300, "a new co-op has room to start");
        open.Should().BeInAscendingOrder();
        open.Should().OnlyHaveUniqueItems("every level opens some tillable ground");
    }

    private static MapSpec Spec()
    {
        _ = GameMaps.TryGet(GameMaps.CoopFarm, out MapSpec? spec).Should().BeTrue();
        return spec!;
    }

    private static TileMap Load(MapSpec spec) => TmxReader.Parse(File.ReadAllText(RepoFiles.Path("art", "own", "Maps", spec.TmxFile)));
}

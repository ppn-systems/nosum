// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.UnitTests.Maps;

/// <summary>Co-op land (docs/adr/ADR-020): the map entry in the catalogue, its gate and arrival tiles on the real placeholder maps, and the audit reason codes.</summary>
public sealed class CoopLandMapTests
{
    private static readonly Guid s_coop = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static TileMap Load(MapSpec spec) => TmxReader.Parse(File.ReadAllText(RepoFiles.Path("assets", "Maps", spec.TmxFile)));

    private static MapSpec Spec(string id)
    {
        _ = GameMaps.TryGet(id, out MapSpec? spec).Should().BeTrue();
        return spec!;
    }

    [Fact]
    public void CoopFarm_IsFarmableCoopLand_ThatIsNeitherPrivateNorShared()
    {
        MapSpec land = Spec(GameMaps.CoopFarm);

        (land.IsCoop, land.Farmable, land.IsPrivate, land.Placeable).Should().Be((true, true, false, false));
        GameMaps.All.Where(m => m.IsCoop).Should().ContainSingle();
        Spec(GameMaps.Farm).TmxFile.Should().NotBe(land.TmxFile, "co-op land is a different map from the personal farm");
    }

    [Fact]
    public void OfCoop_KeysTheInstanceByTheCoop_NotByAPlayer()
    {
        MapInstance land = MapInstance.OfCoop(GameMaps.CoopFarm, s_coop);

        land.IsPrivate.Should().BeTrue("it has one owner");
        land.Key.Should().Be("coopfarm@33333333333333333333333333333333");
        land.Should().NotBe(MapInstance.OfCoop(GameMaps.CoopFarm, Guid.NewGuid()));
    }

    [Fact]
    public void TownGate_IsAWalkableTownTileWhoseExitLeadsToTheCoopLandArrival()
    {
        MapSpec town = Spec(GameMaps.Town);
        TileMap map = Load(town);
        WalkGrid grid = town.BuildWalkGrid(map);

        grid.IsBlocked(CoopLand.TownGate.X, CoopLand.TownGate.Y).Should().BeFalse();
        grid.IsBlocked(CoopLand.TownArrival.X, CoopLand.TownArrival.Y).Should().BeFalse();
        CoopLand.TownArrival.Should().NotBe(CoopLand.TownGate, "arriving on the gate would bounce a member straight back in");
        town.ResolveWarps(map).Should().Contain(new MapWarp(CoopLand.TownGate.X, CoopLand.TownGate.Y, GameMaps.CoopFarm, CoopLand.LandArrival.X, CoopLand.LandArrival.Y));
    }

    [Fact]
    public void LandGate_IsAWalkableLandTileWhoseExitLeadsBackToTown_AndArrivalIsNotOnIt()
    {
        MapSpec land = Spec(GameMaps.CoopFarm);
        TileMap map = Load(land);
        WalkGrid grid = land.BuildWalkGrid(map);
        TillGrid till = TillGrid.FromMap(map, grid);

        grid.IsBlocked(CoopLand.LandGate.X, CoopLand.LandGate.Y).Should().BeFalse();
        (double ax, double ay, _) = WarpRule.Arrival(grid, CoopLand.LandArrival.X, CoopLand.LandArrival.Y);
        MoveValidator.FootBlocked(grid, ax, ay).Should().BeFalse();
        WarpRule.TileX(ax, grid.TileSize).Should().Be(CoopLand.LandArrival.X);
        till.IsTillable(CoopLand.LandArrival.X, CoopLand.LandArrival.Y).Should().BeFalse("the arrival point is bare ground, not a plot");
        CoopLand.LandArrival.Should().NotBe(CoopLand.LandGate);
        land.ResolveWarps(map).Should().Contain(new MapWarp(CoopLand.LandGate.X, CoopLand.LandGate.Y, GameMaps.Town, CoopLand.TownArrival.X, CoopLand.TownArrival.Y));
        Enumerable.Range(0, grid.Width).SelectMany(x => Enumerable.Range(0, grid.Height).Select(y => (x, y))).Count(t => till.IsTillable(t.x, t.y)).Should().BeGreaterThan(200, "there is room to farm");
    }

    [Theory]
    [InlineData("coop.deposit", true)]
    [InlineData("coop.withdraw", true)]
    [InlineData(CoopLand.HarvestReason, false)]
    [InlineData(CoopLand.PlantReason, false)]
    public void IsWarehouseReason_OnlyDepositsAndWithdrawalsCountAsContributions(string reason, bool expected)
        => CoopLand.IsWarehouseReason(reason).Should().Be(expected);

    [Theory]
    [InlineData("farm.till", CoopLand.TillReason)]
    [InlineData("farm.plant", CoopLand.PlantReason)]
    [InlineData("farm.water", CoopLand.WaterReason)]
    [InlineData("farm.harvest", CoopLand.HarvestReason)]
    public void AuditReason_MapsEachFarmActionToItsCoopCode(string farmReason, string expected)
        => CoopLand.AuditReason(farmReason).Should().Be(expected);
}

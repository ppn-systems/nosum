// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Xunit;

namespace Nosum.UnitTests.Maps;

/// <summary>The real placeholder maps and the catalogue that names them.</summary>
public sealed class GameMapsTests
{
    private static TileMap Load(MapSpec spec) => TmxReader.Parse(File.ReadAllText(RepoFiles.Path("assets", "Maps", spec.TmxFile)));

    public static TheoryData<string> MapIds()
    {
        TheoryData<string> ids = [];
        foreach (MapSpec spec in GameMaps.All)
        {
            ids.Add(spec.Id);
        }

        return ids;
    }

    [Fact]
    public void All_Ids_AreUniqueLowerCaseKeys()
    {
        GameMaps.All.Select(m => m.Id).Should().OnlyHaveUniqueItems().And.OnlyContain(id => id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)));
        GameMaps.TryGet(GameMaps.DefaultId, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("Farm.tmx")]
    [InlineData("FARM")]
    [InlineData("")]
    [InlineData(null)]
    public void TryGet_NotAnExactId_IsRefused(string? id)
    {
        GameMaps.TryGet(id, out MapSpec? map).Should().BeFalse();
        map.Should().BeNull();
    }

    [Fact]
    public void Resolve_TiledMapName_IgnoresCase()
    {
        GameMaps.Resolve("BusStop")!.Id.Should().Be(GameMaps.BusStop);
        GameMaps.Resolve("Town").Should().BeNull();
    }

    [Fact]
    public void MapSpec_FileWithPath_Throws()
    {
        Action create = () => _ = new MapSpec("x", "../Farm.tmx", 0, 0);

        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [MemberData(nameof(MapIds))]
    public void ResolveWarps_EveryExit_StartsAndEndsOnWalkableTiles(string id)
    {
        _ = GameMaps.TryGet(id, out MapSpec? spec);
        TileMap map = Load(spec!);
        WalkGrid grid = spec!.BuildWalkGrid(map);

        List<MapWarp> warps = spec.ResolveWarps(map);

        warps.Should().NotBeEmpty();
        foreach (MapWarp warp in warps)
        {
            _ = GameMaps.TryGet(warp.TargetMap, out MapSpec? target).Should().BeTrue();
            WalkGrid targetGrid = target!.BuildWalkGrid(Load(target));
            (double x, double y, _) = WarpRule.Arrival(targetGrid, warp.TargetX, warp.TargetY);
            MoveValidator.FootBlocked(targetGrid, x, y).Should().BeFalse($"{id} -> {warp}");
        }

        // At least one source tile of every exit group can be reached (the others may sit in walls).
        warps.Any(w => WarpRule.TryEdge(w, grid, out int sx, out int sy, out _) && !grid.IsBlocked(sx, sy)).Should().BeTrue();
    }

    [Fact]
    public void ResolveWarps_Farm_DropsExitsToMapsTheWorldDoesNotHost()
    {
        _ = GameMaps.TryGet(GameMaps.Farm, out MapSpec? farm);
        TileMap map = Load(farm!);
        map.Properties["Warp"].Should().Contain("Backwoods").And.Contain("FarmCave");

        List<MapWarp> warps = farm!.ResolveWarps(map);

        warps.Select(w => w.TargetMap).Distinct().Should().BeEquivalentTo(GameMaps.BusStop, GameMaps.Forest, GameMaps.FarmHouse);
    }

    [Theory]
    [InlineData(GameMaps.FarmHouse)]
    [InlineData(GameMaps.BusStop)]
    [InlineData(GameMaps.Forest)]
    public void ResolveWarps_NeighbourOfTheFarm_HasWayBack(string neighbour)
    {
        _ = GameMaps.TryGet(neighbour, out MapSpec? spec);

        spec!.ResolveWarps(Load(spec)).Should().Contain(w => w.TargetMap == GameMaps.Farm);
    }

    [Fact]
    public void BuildWalkGrid_Farm_FarmhouseDoorIsWalkableButItsWallsAreNot()
    {
        _ = GameMaps.TryGet(GameMaps.Farm, out MapSpec? farm);

        WalkGrid grid = farm!.BuildWalkGrid(Load(farm));

        grid.IsBlocked(64, 15).Should().BeFalse();
        grid.IsBlocked(63, 15).Should().BeTrue();
        grid.IsBlocked(64, 12).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(MapIds))]
    public void Assets_EveryTilesheetAndMapFile_IsLinkedIntoClientAndServer(string id)
    {
        _ = GameMaps.TryGet(id, out MapSpec? spec);
        string wasm = File.ReadAllText(RepoFiles.Path("wasm", "Nosum.Wasm", "Nosum.Wasm.csproj"));
        string host = File.ReadAllText(RepoFiles.Path("server", "Nosum.Host", "Nosum.Host.csproj"));

        wasm.Should().Contain(@"assets\Maps\" + spec!.TmxFile);
        host.Should().Contain(@"assets\Maps\" + spec.TmxFile);
        foreach (TileSet set in Load(spec).TileSets)
        {
            string image = set.ImageSource.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? set.ImageSource : set.ImageSource + ".png";
            File.Exists(RepoFiles.Path("assets", "Maps", image)).Should().BeTrue($"{image} of {id}");
            wasm.Should().Contain(@"assets\Maps\" + image);
        }
    }
}

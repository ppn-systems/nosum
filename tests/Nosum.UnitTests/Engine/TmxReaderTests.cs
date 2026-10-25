// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using Nosum.Core;
using Xunit;

namespace Nosum.UnitTests.Engine;

public sealed class TmxReaderTests
{
    private const string Map = """
        <?xml version="1.0"?>
        <map version="1.4" orientation="orthogonal" width="3" height="2" tilewidth="16" tileheight="16">
          <tileset firstgid="1" name="ground" tilewidth="16" tileheight="16" tilecount="4" columns="2">
            <image source="ground_sheet" width="32" height="32" />
            <tile id="0"><properties><property name="Type" value="Grass" /></properties></tile>
          </tileset>
          <tileset firstgid="5" name="props" tilewidth="16" tileheight="16" tilecount="8" columns="4">
            <image source="props.png" width="64" height="32" />
          </tileset>
          <layer id="1" name="Back" width="3" height="2">
            <data encoding="csv">
        1,2,3,
        4,1,2
        </data>
          </layer>
          <layer id="2" name="Buildings" width="3" height="2">
            <data encoding="csv">
        0,5,0,
        0,0,2147483660
        </data>
          </layer>
        </map>
        """;

    [Fact]
    public void Parse_Map_ReadsSizeTileSetsAndLayersInOrder()
    {
        TileMap map = TmxReader.Parse(Map);

        map.Width.Should().Be(3);
        map.Height.Should().Be(2);
        map.TileWidth.Should().Be(16);
        map.TileSets.Should().HaveCount(2);
        map.TileSets[0].ImageSource.Should().Be("ground_sheet");
        map.TileSets[1].Columns.Should().Be(4);
        map.Layers.Should().HaveCount(2);
        map.Layers[0].Name.Should().Be("Back");
        map.Layers[0].Gids.Should().Equal(1, 2, 3, 4, 1, 2);
    }

    [Fact]
    public void Parse_FlippedTile_ClearsFlipBits()
    {
        TileMap map = TmxReader.Parse(Map);

        map.FindLayer("Buildings")!.Gids[5].Should().Be(12);
    }

    [Fact]
    public void FindTileSet_Gid_ReturnsOwningSetOrMinusOne()
    {
        TileMap map = TmxReader.Parse(Map);

        map.FindTileSet(4).Should().Be(0);
        map.FindTileSet(5).Should().Be(1);
        map.FindTileSet(12).Should().Be(1);
        map.FindTileSet(13).Should().Be(-1);
    }

    [Fact]
    public void Parse_WrongTileCount_Throws()
    {
        string broken = Map.Replace("4,1,2", "4,1", StringComparison.Ordinal);

        Action act = () => TmxReader.Parse(broken);

        act.Should().Throw<InvalidDataException>();
    }
}

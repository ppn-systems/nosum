// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Xunit;

namespace Nosum.UnitTests.Maps;

public sealed class WarpParserTests
{
    [Fact]
    public void Parse_GroupsOfFive_ReadsEveryExitInOrder()
    {
        List<MapWarp> warps = WarpParser.Parse("80 15 BusStop 11 23  -1 22 Farm 79 17\n40 65 Forest 68 0");

        warps.Should().Equal(
            new MapWarp(80, 15, "BusStop", 11, 23),
            new MapWarp(-1, 22, "Farm", 79, 17),
            new MapWarp(40, 65, "Forest", 68, 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NoValue_HasNoExits(string? value) => WarpParser.Parse(value).Should().BeEmpty();

    [Theory]
    [InlineData("80 15 BusStop 11")]
    [InlineData("80 15 BusStop 11 23 1")]
    [InlineData("eighty 15 BusStop 11 23")]
    [InlineData("80 15 BusStop 11 2.5")]
    public void Parse_Malformed_Throws(string value)
    {
        Action parse = () => WarpParser.Parse(value);

        parse.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_TmxMapProperty_IsReadByTheTmxReader()
    {
        const string Xml = """
            <?xml version="1.0"?>
            <map orientation="orthogonal" width="1" height="1" tilewidth="16" tileheight="16">
              <properties><property name="Warp" type="string" value="1 0 B 2 3 0 -1 C 4 5" /></properties>
              <tileset firstgid="1" name="t" tilewidth="16" tileheight="16" tilecount="1" columns="1"><image source="t" width="16" height="16" /></tileset>
              <layer id="1" name="Back" width="1" height="1"><data encoding="csv">1</data></layer>
            </map>
            """;

        TileMap map = TmxReader.Parse(Xml);

        WarpParser.Parse(map.Properties[WarpParser.PropertyName]).Should().HaveCount(2).And.Contain(new MapWarp(0, -1, "C", 4, 5));
    }
}

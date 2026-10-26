// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using Nosum.Core;
using Nosum.MapGen;
using Xunit;

namespace Nosum.UnitTests.Tools;

/// <summary>The layout compiler (docs/adr/ADR-042 D4): a text grid with a legend becomes a <c>.tmx</c> the game's loader reads unchanged.</summary>
public sealed class LayoutCompilerTests
{
    private const string Terrain = """
        name terrain
        image terrain.png
        tilewidth 16
        tileheight 16
        columns 4
        tilecount 8
        tile 1 Water
        tile 2 Passable
        """;

    private const string Sample = """
        // a four by three room
        legend
        a terrain 0
        b terrain 1
        c terrain 2
        property Warp 1 2 Town 3 0
        layer Back
        aaaa
        abba
        aaaa
        layer Buildings
        ....
        .c..
        ....
        """;

    private static TilesetData Resolve(string name)
        => name == "terrain"
            ? TilesetData.Parse(Terrain, "terrain.tileset")
            : throw new InvalidDataException($"no tileset {name}");

    [Fact]
    public void Compile_SampleLayout_RoundTripsThroughTmxReader()
    {
        string tmx = LayoutCompiler.Compile(Sample, "sample.txt", Resolve);

        TileMap map = TmxReader.Parse(tmx);

        map.Width.Should().Be(4);
        map.Height.Should().Be(3);
        map.TileSets.Should().ContainSingle().Which.Name.Should().Be("terrain");
        map.TileSets[0].FirstGid.Should().Be(1);
        map.FindLayer("Back")!.Gids.Should().Equal(1, 1, 1, 1, 1, 2, 2, 1, 1, 1, 1, 1);
        map.FindLayer("Buildings")!.Gids.Should().Equal(0, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0);
        map.GetTraits(2).Should().Be(TileTraits.Water, "the legend's b is tile 1 of the sheet, which is water");
        map.GetTraits(3).Should().Be(TileTraits.Passable);
        map.Properties["Warp"].Should().Be("1 2 Town 3 0");
    }

    [Fact]
    public void Compile_SameLayout_IsByteIdentical()
    {
        string first = LayoutCompiler.Compile(Sample, "sample.txt", Resolve);
        string second = LayoutCompiler.Compile(Sample, "sample.txt", Resolve);

        second.Should().Be(first);
        first.Should().EndWith("</map>\n");
    }

    [Fact]
    public void Compile_MalformedLegend_NamesTheLine()
    {
        string text = Sample.Replace("b terrain 1", "bb terrain 1", StringComparison.Ordinal);

        Action act = () => LayoutCompiler.Compile(text, "sample.txt", Resolve);

        act.Should().Throw<InvalidDataException>().WithMessage("sample.txt:4: a legend entry is '<character> <tileset> <tile index>'*");
    }

    [Fact]
    public void Compile_LegendIndexIsNotANumber_NamesTheLine()
    {
        string text = Sample.Replace("c terrain 2", "c terrain two", StringComparison.Ordinal);

        Action act = () => LayoutCompiler.Compile(text, "sample.txt", Resolve);

        act.Should().Throw<InvalidDataException>().WithMessage("sample.txt:5: tile index 'two' is not a whole number");
    }

    [Fact]
    public void Compile_LegendNamesAnUnknownTileset_NamesTheLine()
    {
        string text = Sample.Replace("a terrain 0", "a grass 0", StringComparison.Ordinal);

        Action act = () => LayoutCompiler.Compile(text, "sample.txt", Resolve);

        act.Should().Throw<InvalidDataException>().WithMessage("sample.txt:3: no tileset grass");
    }

    [Fact]
    public void Compile_LegendIndexOutsideTheSheet_NamesTheLine()
    {
        string text = Sample.Replace("c terrain 2", "c terrain 9", StringComparison.Ordinal);

        Action act = () => LayoutCompiler.Compile(text, "sample.txt", Resolve);

        act.Should().Throw<InvalidDataException>().WithMessage("sample.txt:5: tile 9 is outside 'terrain'*");
    }

    [Fact]
    public void Compile_RaggedRow_NamesTheLine()
    {
        string text = Sample.Replace("abba", "abb", StringComparison.Ordinal);

        Action act = () => LayoutCompiler.Compile(text, "sample.txt", Resolve);

        act.Should().Throw<InvalidDataException>().WithMessage("sample.txt:9: the row has 3 tiles, the first row has 4");
    }

    [Fact]
    public void Compile_CharacterWithoutLegendEntry_NamesTheLine()
    {
        string text = Sample.Replace("abba", "abza", StringComparison.Ordinal);

        Action act = () => LayoutCompiler.Compile(text, "sample.txt", Resolve);

        act.Should().Throw<InvalidDataException>().WithMessage("sample.txt:9: character 'z' has no legend entry");
    }

    [Fact]
    public void Compile_LayerWithFewerRows_IsRejected()
    {
        string text = Sample.Replace("layer Buildings\n....\n.c..\n....", "layer Buildings\n....\n.c..", StringComparison.Ordinal);

        Action act = () => LayoutCompiler.Compile(text, "sample.txt", Resolve);

        act.Should().Throw<InvalidDataException>().WithMessage("sample.txt:11: layer 'Buildings' has 2 rows, the first layer has 3");
    }

    [Fact]
    public void Compile_PropertyAfterALayer_IsRejected()
    {
        string text = Sample.Replace("layer Back", "layer Back\nproperty Warp 1 2 Town 3 0", StringComparison.Ordinal);

        Action act = () => LayoutCompiler.Compile(text, "sample.txt", Resolve);

        act.Should().Throw<InvalidDataException>().WithMessage("sample.txt:8: map properties must come before the first layer");
    }
}

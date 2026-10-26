// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using Nosum.Core;
using Nosum.MapGen;
using Xunit;

namespace Nosum.UnitTests.Tools;

/// <summary>The own tileset data file (docs/adr/ADR-042 D3): its name, its grid and the traits the rules read on each tile.</summary>
public sealed class TilesetDataTests
{
    private const string Sheet = """
        // a sample sheet
        name terrain
        image terrain.png
        tilewidth 16
        tileheight 16
        columns 4
        tilecount 8
        tile 0 Diggable
        tile 1-2 Passable
        tile 3 Water
        tile 5 Impassable
        """;

    [Fact]
    public void Parse_SampleSheet_ReadsGridNameAndTraits()
    {
        TilesetData set = TilesetData.Parse(Sheet, "terrain.tileset");

        set.Name.Should().Be("terrain");
        set.Image.Should().Be("terrain.png");
        set.TileWidth.Should().Be(16);
        set.Columns.Should().Be(4);
        set.TileCount.Should().Be(8);
        set.Rows.Should().Be(2);
        set.Traits[0].Should().Be(TileTraits.Diggable);
        set.Traits[1].Should().Be(TileTraits.Passable);
        set.Traits[2].Should().Be(TileTraits.Passable);
        set.Traits[3].Should().Be(TileTraits.Water);
        set.Traits[5].Should().Be(TileTraits.Impassable);
        set.Traits.Should().NotContainKey(4, "a tile without a trait line has none");
    }

    [Fact]
    public void Parse_UnknownTrait_NamesTheLine()
    {
        string text = "name terrain\ntile 0 Slippery\nimage terrain.png\ntilewidth 16\ntileheight 16\ncolumns 4\ntilecount 8\n";

        Action act = () => TilesetData.Parse(text, "terrain.tileset");

        act.Should().Throw<InvalidDataException>().WithMessage("terrain.tileset:2: unknown trait 'Slippery'*");
    }

    [Fact]
    public void Parse_PassableAndImpassableOnOneTile_IsRejected()
    {
        string text = Sheet + "\ntile 1 Impassable\n";

        Action act = () => TilesetData.Parse(text, "terrain.tileset");

        act.Should().Throw<InvalidDataException>().WithMessage("*tile 1 is both Passable and Impassable*");
    }

    [Fact]
    public void Parse_TileOutsideTheSheet_IsRejected()
    {
        string text = Sheet.Replace("tile 5 Impassable", "tile 8 Water", StringComparison.Ordinal);

        Action act = () => TilesetData.Parse(text, "terrain.tileset");

        act.Should().Throw<InvalidDataException>().WithMessage("*tile 8 is outside the sheet, which has 8 tiles*");
    }

    [Fact]
    public void Parse_MissingKeyword_NamesTheKeyword()
    {
        string text = Sheet.Replace("columns 4\n", string.Empty, StringComparison.Ordinal);

        Action act = () => TilesetData.Parse(text, "terrain.tileset");

        act.Should().Throw<InvalidDataException>().WithMessage("*'columns' is missing*");
    }

    [Fact]
    public void Parse_KeywordGivenTwice_IsRejected()
    {
        string text = Sheet + "\ncolumns 2\n";

        Action act = () => TilesetData.Parse(text, "terrain.tileset");

        act.Should().Throw<InvalidDataException>().WithMessage("*'columns' is given twice*");
    }
}

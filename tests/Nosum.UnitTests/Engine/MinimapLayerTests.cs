// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using FluentAssertions;
using Nosum.Core;
using Nosum.Rendering;
using Xunit;

namespace Nosum.UnitTests.Engine;

public sealed class MinimapLayerTests
{
    // Tiles (local id = gid - 1): gid 1 is plain ground, gid 2 is water.
    private static TileMap Map(int width, int height, params int[] gids)
    {
        Dictionary<int, KeyValuePair<string, string>[]> props = new()
        {
            [1] = [new("Water", "T")],
        };
        TileSet set = new(1, "t", "t", 2, 2, new TileProperties(2, props, new Dictionary<int, TileAnimation>()));
        return new TileMap(width, height, 16, 16, [set], [new TileLayer("Ground", gids)]);
    }

    private static int[] Draws(RenderCommandBuffer buffer) => MemoryMarshal.Cast<byte, int>(buffer.Encode()).ToArray();

    // Each draw is nine ints after the two header ints: texture, srcX, srcY, width, height, dstX, dstY, flags, tint.
    private static IEnumerable<int> TintsOf(int[] ints, int count)
        => Enumerable.Range(0, count).Select(i => ints[2 + (i * RenderCommandBuffer.IntsPerCommand) + 8]);

    [Fact]
    public void Build_TwoRowsOfGround_RecordsOneRunPerRow()
    {
        MinimapLayer minimap = new() { Visible = true };
        minimap.Build(Map(4, 2, 1, 1, 1, 1, 1, 1, 1, 1), "town", MinimapPalette.Default);
        RenderCommandBuffer buffer = new(64);
        buffer.Begin(1);

        minimap.Draw(buffer, 320, 240, 0, 0, 0x14171CCC, 0x0B0D10FF);

        // The border, the backing and one run per row.
        buffer.Count.Should().Be(4);
    }

    [Fact]
    public void Build_WaterCell_UsesTheWaterColour()
    {
        MinimapLayer minimap = new() { Visible = true };
        minimap.Build(Map(2, 1, 1, 2), "pond", MinimapPalette.Default);
        RenderCommandBuffer buffer = new(64);
        buffer.Begin(1);

        minimap.Draw(buffer, 320, 240, 0, 0, 0x14171CCC, 0x0B0D10FF);

        TintsOf(Draws(buffer), buffer.Count).Should().Contain(MinimapPalette.Default.Water);
    }

    [Fact]
    public void Draw_WhenHidden_RecordsNothing()
    {
        MinimapLayer minimap = new();
        minimap.Build(Map(2, 2, 1, 1, 1, 1), "room", MinimapPalette.Default);
        RenderCommandBuffer buffer = new(64);
        buffer.Begin(1);

        minimap.Draw(buffer, 320, 240, 0, 0, 0x14171CCC, 0x0B0D10FF);

        buffer.Count.Should().Be(0);
    }

    [Fact]
    public void Draw_BeforeAnyBuild_RecordsNothing()
    {
        MinimapLayer minimap = new() { Visible = true };
        RenderCommandBuffer buffer = new(64);
        buffer.Begin(1);

        minimap.Draw(buffer, 320, 240, 0, 0, 0x14171CCC, 0x0B0D10FF);

        buffer.Count.Should().Be(0);
    }

    [Fact]
    public void Draw_RightInset_KeepsTheBoxClearOfTheHudColumn()
    {
        MinimapLayer minimap = new() { Visible = true };
        minimap.Build(Map(4, 2, 1, 1, 1, 1, 1, 1, 1, 1), "town", MinimapPalette.Default);
        RenderCommandBuffer buffer = new(64);
        buffer.Begin(1);

        minimap.Draw(buffer, 320, 240, 50, 0, 0x14171CCC, 0x0B0D10FF);

        // The first draw is the border; its right edge stops the inset and the margin short of the screen's edge.
        int[] ints = Draws(buffer);
        int borderLeft = ints[2 + 5];
        borderLeft.Should().Be(320 - 50 - MinimapLayer.Margin - minimap.BoxWidth);
    }

    [Fact]
    public void Draw_ScreenTooNarrowForTheBox_RecordsNothing()
    {
        MinimapLayer minimap = new() { Visible = true };
        minimap.Build(Map(4, 2, 1, 1, 1, 1, 1, 1, 1, 1), "town", MinimapPalette.Default);
        RenderCommandBuffer buffer = new(64);
        buffer.Begin(1);

        minimap.Draw(buffer, 20, 240, 0, 0, 0x14171CCC, 0x0B0D10FF);

        buffer.Count.Should().Be(0);
    }

    [Fact]
    public void AddDot_PlayerOnAnotherTile_RecordsOneDotForTheFrame()
    {
        MinimapLayer minimap = new() { Visible = true };
        minimap.Build(Map(4, 2, 1, 1, 1, 1, 1, 1, 1, 1), "town", MinimapPalette.Default);
        minimap.BeginDots();
        minimap.AddDot(2.5, 1.5, unchecked((int)0xFFF27DFFu));
        RenderCommandBuffer buffer = new(64);
        buffer.Begin(1);

        minimap.Draw(buffer, 320, 240, 0, 0, 0x14171CCC, 0x0B0D10FF);

        // Two for the box, two runs, and the dot.
        buffer.Count.Should().Be(5);
        TintsOf(Draws(buffer), buffer.Count).Last().Should().Be(unchecked((int)0xFFF27DFFu));
    }

    [Fact]
    public void AddMarker_SurvivesBeginDots_UntilTheNextBuild()
    {
        MinimapLayer minimap = new() { Visible = true };
        minimap.Build(Map(4, 2, 1, 1, 1, 1, 1, 1, 1, 1), "town", MinimapPalette.Default);
        minimap.AddMarker(0.5, 0.5, unchecked((int)0xFF9F43FFu));
        minimap.BeginDots();
        RenderCommandBuffer first = new(64);
        first.Begin(1);
        minimap.Draw(first, 320, 240, 0, 0, 0x14171CCC, 0x0B0D10FF);

        RenderCommandBuffer second = new(64);
        second.Begin(1);
        minimap.Draw(second, 320, 240, 0, 0, 0x14171CCC, 0x0B0D10FF);

        TintsOf(Draws(second), second.Count).Should().Contain(unchecked((int)0xFF9F43FFu));
        first.Count.Should().Be(second.Count);
    }

    [Fact]
    public void Build_SameKey_KeepsTheCachedGround()
    {
        MinimapLayer minimap = new() { Visible = true };
        minimap.Build(Map(4, 2, 1, 1, 1, 1, 1, 1, 1, 1), "town", MinimapPalette.Default);

        bool rebuilt = minimap.Build(Map(4, 2, 2, 2, 2, 2, 2, 2, 2, 2), "town", MinimapPalette.Default);

        rebuilt.Should().BeTrue();
        RenderCommandBuffer buffer = new(64);
        buffer.Begin(1);
        minimap.Draw(buffer, 320, 240, 0, 0, 0x14171CCC, 0x0B0D10FF);
        TintsOf(Draws(buffer), buffer.Count).Should().NotContain(MinimapPalette.Default.Water);
    }

    [Fact]
    public void Build_MapLargerThanTheCache_ReturnsFalseAndShowsNothing()
    {
        MinimapLayer minimap = new(maxCells: 2) { Visible = true };

        bool built = minimap.Build(Map(2, 2, 1, 1, 1, 1), "big", MinimapPalette.Default);

        built.Should().BeFalse();
        minimap.Key.Should().BeNull();
    }

    [Fact]
    public void Cell_LargeRoom_IsCappedAtMaxCell()
    {
        MinimapLayer minimap = new() { Visible = true };

        minimap.Build(Map(2, 2, 1, 1, 1, 1), "room", MinimapPalette.Default);

        minimap.Cell.Should().Be(MinimapLayer.MaxCell);
    }
}

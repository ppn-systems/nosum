// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using SkiaSharp;
using Xunit;

namespace Nosum.IntegrationTests.Tools;

/// <summary>
/// The farmer layer sheets are drawn separately and stacked by the client (wasm/Nosum.Wasm/Game/FarmerSprite.cs). These tests stack them the
/// same way and check by bounding boxes that the layers meet in every walk frame: no gap between head and hair or hat, none between body and pants.
/// </summary>
public sealed class FarmerLayersTests
{
    // Mirrors FarmerSprite: 16 x 32 frames, walk columns, hair 2 px lower, shirt piece at (4, 15), hat 20 x 20 two pixels to the left, 1 px bob on step columns.
    private const int W = 16;
    private const int H = 32;
    private const int HairOffsetY = 2;
    private const int ShirtX = 4;
    private const int ShirtY = 15;
    private const int HatSize = 20;
    private const int Canvas = 24;
    private const int Margin = 4;
    private static readonly int[] s_shirts = [1, 3, 7, 11];
    private static readonly int[] s_hats = [3, 5];

    public static TheoryData<int, int> Frames()
    {
        TheoryData<int, int> data = [];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                data.Add(row, column);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void Stack_EveryWalkFrame_HasNoGapBetweenBodyAndPantsOrHeadAndHair(int row, int column)
    {
        int bob = column == 0 ? 0 : 1;
        for (int style = 0; style < 8; style++)
        {
            bool[,] body = Cell("body.png", column * W, row * H, W, H);
            bool[,] pants = Cell("pants.png", column * W, row * H, W, H);
            bool[,] hair = Shift(Cell("hair.png", style * W, row * H, W, H), 0, HairOffsetY + bob);
            Box head = Box.Of(body, 0, 0, W, 17 + bob);
            Box torso = Box.Of(body, 0, 15 + bob, W, 23);

            // body and pants: the trousers start at or above the torso's lowest row, so the stack is one connected figure
            Box.Of(pants, 0, 0, W, H).Top.Should().BeLessThanOrEqualTo(torso.Bottom + 1, $"pants must meet the body (row {row}, column {column})");

            // head and hair: the hair covers the top of the head and reaches into it
            Box hairBox = Box.Of(hair, 0, 0, W, H);
            hairBox.Top.Should().BeLessThanOrEqualTo(head.Top + 1, $"hair must reach the top of the head (style {style}, row {row}, column {column})");
            hairBox.Bottom.Should().BeGreaterThanOrEqualTo(head.Top + 4, $"hair must overlap the head (style {style}, row {row}, column {column})");
            hairBox.Left.Should().BeLessThanOrEqualTo(head.Left + 2);
            hairBox.Right.Should().BeGreaterThanOrEqualTo(head.Right - 2);
        }
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void Stack_EveryWalkFrame_IsOneFigureStandingOnTheFeetRow(int row, int column)
    {
        foreach (int shirt in s_shirts)
        {
            foreach (int hat in new[] { -1, 3, 5 })
            {
                bool[,] image = Compose(row, column, shirt, 0, hat, out _);
                Box all = Box.Of(image, 0, 0, Canvas, Canvas + 16);
                all.Bottom.Should().Be(Margin + H - 1, "the boots stand on the frame's bottom edge");
                Components(image).Should().Be(1, $"the stacked layers must form one figure (shirt {shirt}, hat {hat}, row {row}, column {column})");
                for (int y = all.Top; y <= all.Bottom; y++)
                {
                    RowHasPixel(image, y).Should().BeTrue($"row {y} is empty: a gap in the figure (shirt {shirt}, hat {hat}, row {row}, column {column})");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void Hats_EveryDirection_SitOnTheHead(int row, int column)
    {
        int bob = column == 0 ? 0 : 1;
        bool[,] body = Cell("body.png", column * W, row * H, W, H);
        Box head = Box.Of(body, 0, 0, W, 17 + bob);
        int[] hatRows = row == 0 ? [0] : row == 1 ? [1, 2] : [3];
        foreach (int hat in s_hats)
        {
            foreach (int hatRow in hatRows)
            {
                bool[,] cell = Shift(Cell("hats.png", hat % 12 * HatSize, (hat / 12 * HatSize * 4) + (hatRow * HatSize), HatSize, HatSize), -2, bob);
                Box box = Box.Of(cell, -2, 0, W + 2, H);
                box.Bottom.Should().BeGreaterThanOrEqualTo(head.Top + 3, $"hat {hat} must reach down onto the head");
                box.Top.Should().BeLessThanOrEqualTo(head.Top, $"hat {hat} must start at or above the head's top");
                box.Left.Should().BeLessThanOrEqualTo(head.Left);
                box.Right.Should().BeGreaterThanOrEqualTo(head.Right);
            }
        }
    }

    [Fact]
    public void Sheets_UnusedCells_AreTransparent()
    {
        // pants style 1 and hair style 8 are not drawn; the hat and shirt cells beside the used ones stay empty
        Box.Of(Cell("pants.png", 192, 0, W, H), 0, 0, W, H).IsEmpty.Should().BeTrue();
        Box.Of(Cell("shirts.png", 0, 0, 8, 8), 0, 0, 8, 8).IsEmpty.Should().BeTrue("shirt 0 is not worn");
        Box.Of(Cell("hats.png", 0, 0, HatSize, HatSize), 0, 0, HatSize, HatSize).IsEmpty.Should().BeTrue("hat 0 is not worn");
    }

    private static bool[,] Compose(int row, int column, int shirt, int style, int hat, out int bob)
    {
        bob = column == 0 ? 0 : 1;
        bool[,] image = new bool[Canvas, Canvas + 16];
        Stamp(image, Cell("body.png", column * W, row * H, W, H), 0, 0);
        Stamp(image, Cell("pants.png", column * W, row * H, W, H), 0, 0);
        int[] pieceOffset = [0, 8, 24];
        Stamp(image, Cell("shirts.png", shirt % 16 * 8, (shirt / 16 * 32) + pieceOffset[row], 8, 8), ShirtX, ShirtY + bob);
        Stamp(image, Cell("body.png", (column + 6) * W, row * H, W, H), 0, 0);
        Stamp(image, Cell("hair.png", style * W, row * H, W, H), 0, HairOffsetY + bob);
        if (hat >= 0)
        {
            int hatRow = row == 0 ? 0 : row == 1 ? 1 : 3;
            Stamp(image, Cell("hats.png", hat % 12 * HatSize, (hat / 12 * HatSize * 4) + (hatRow * HatSize), HatSize, HatSize), -2, bob);
        }

        return image;
    }

    private static void Stamp(bool[,] image, bool[,] cell, int x, int y)
    {
        for (int j = 0; j < cell.GetLength(1); j++)
        {
            for (int i = 0; i < cell.GetLength(0); i++)
            {
                int px = Margin + x + i;
                int py = Margin + y + j;
                if (cell[i, j] && px >= 0 && py >= 0 && px < image.GetLength(0) && py < image.GetLength(1))
                {
                    image[px, py] = true;
                }
            }
        }
    }

    private static bool[,] Shift(bool[,] cell, int dx, int dy)
    {
        int w = cell.GetLength(0);
        int h = cell.GetLength(1);
        bool[,] shifted = new bool[w + 4, h + 4];
        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                if (cell[i, j] && i + dx >= 0 && j + dy >= 0 && i + dx < shifted.GetLength(0) && j + dy < shifted.GetLength(1))
                {
                    shifted[i + dx, j + dy] = true;
                }
            }
        }

        return shifted;
    }

    private static bool RowHasPixel(bool[,] image, int y)
    {
        for (int x = 0; x < image.GetLength(0); x++)
        {
            if (image[x, y])
            {
                return true;
            }
        }

        return false;
    }

    private static int Components(bool[,] image)
    {
        int w = image.GetLength(0);
        int h = image.GetLength(1);
        bool[,] seen = new bool[w, h];
        int count = 0;
        Stack<(int X, int Y)> open = new();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!image[x, y] || seen[x, y])
                {
                    continue;
                }

                count++;
                open.Push((x, y));
                seen[x, y] = true;
                while (open.Count > 0)
                {
                    (int cx, int cy) = open.Pop();
                    foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = cx + dx;
                        int ny = cy + dy;
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h && image[nx, ny] && !seen[nx, ny])
                        {
                            seen[nx, ny] = true;
                            open.Push((nx, ny));
                        }
                    }
                }
            }
        }

        return count;
    }

    /// <summary>Opaque pixels of a rectangle of one farmer sheet, indexed [x, y].</summary>
    private static bool[,] Cell(string sheet, int x, int y, int w, int h)
    {
        string path = Path.Combine(FindRoot(), "assets", "farmer", sheet);
        using SKBitmap bitmap = SKBitmap.Decode(path);
        bool[,] cell = new bool[w, h];
        for (int j = 0; j < h; j++)
        {
            for (int i = 0; i < w; i++)
            {
                cell[i, j] = bitmap.GetPixel(x + i, y + j).Alpha > 0;
            }
        }

        return cell;
    }

    private static string FindRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root (AGENTS.md) not found above " + AppContext.BaseDirectory);
    }

    private readonly record struct Box(int Left, int Top, int Right, int Bottom)
    {
        public bool IsEmpty => Right < Left;

        public static Box Of(bool[,] cell, int x0, int y0, int x1, int y1)
        {
            int left = int.MaxValue;
            int top = int.MaxValue;
            int right = int.MinValue;
            int bottom = int.MinValue;
            for (int j = 0; j < cell.GetLength(1); j++)
            {
                for (int i = 0; i < cell.GetLength(0); i++)
                {
                    if (!cell[i, j] || j < y0 || j >= y1 || i < x0 || i >= x1)
                    {
                        continue;
                    }

                    left = Math.Min(left, i);
                    right = Math.Max(right, i);
                    top = Math.Min(top, j);
                    bottom = Math.Max(bottom, j);
                }
            }

            return new Box(left, top, right, bottom);
        }
    }
}

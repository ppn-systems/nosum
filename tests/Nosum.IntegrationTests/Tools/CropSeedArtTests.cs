// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using SkiaSharp;
using Xunit;

namespace Nosum.IntegrationTests.Tools;

/// <summary>
/// The stage-0 picture of every crop (frame 0 of its row in <c>crops.png</c>) is a green sprout that reads on the tilled soil. Before world pass 7
/// it was the pack's red sprout, whose colours matched the soil (contrast about 1.0 on the dark soil), so a planted tile looked bare.
/// </summary>
public sealed class CropSeedArtTests
{
    private const int FrameWidth = 16;
    private const int FrameHeight = 32;
    private const int RowWidth = 128;
    private const int CropRows = 21;

    [Fact]
    public void SeedFrame_OfEveryCrop_IsGreenAndHasAVisibleSprout()
    {
        using SKBitmap sheet = SKBitmap.Decode(Path.Combine(FindRoot(), "assets", "sprites", "crops.png"));

        for (int row = 0; row < CropRows; row++)
        {
            int x0 = (row % 2) * RowWidth;
            int y0 = (row / 2) * FrameHeight;
            int opaque = 0;
            int green = 0;
            for (int y = 0; y < FrameHeight; y++)
            {
                for (int x = 0; x < FrameWidth; x++)
                {
                    SKColor c = sheet.GetPixel(x0 + x, y0 + y);
                    if (c.Alpha == 0)
                    {
                        continue;
                    }

                    opaque++;
                    if (c.Green > c.Red && c.Green > c.Blue)
                    {
                        green++;
                    }
                }
            }

            opaque.Should().BeGreaterThanOrEqualTo(10, $"crop row {row} has a seed picture of some size");
            green.Should().Be(opaque, $"every pixel of the seed picture of crop row {row} is green, so it reads on the soil");
        }
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
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Nosum.AtlasPacker;
using SkiaSharp;
using Xunit;

namespace Nosum.IntegrationTests.Tools;

public sealed class AtlasPackerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nosum-atlas-" + Guid.NewGuid().ToString("N"));

    public AtlasPackerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "assets", "crops"));
    }

    private string Assets => Path.Combine(_root, "assets");

    private string Output => Path.Combine(_root, "out");

    [Fact]
    public void Run_Pack_WritesAtlasWhosePixelsMatchTheSources()
    {
        WriteSprite("crops/carrot_stage_0.png", 32, 32, new SKColor(200, 100, 20, 255));
        WriteSprite("crops/carrot_stage_1.png", 32, 48, new SKColor(20, 180, 40, 128));

        int exit = Program.Run(Assets, Output, check: false, TextWriter.Null);

        exit.Should().Be(0);
        string json = File.ReadAllText(Path.Combine(Output, "crops.json"));
        json.Should().Contain("\"carrot_stage_0\"").And.Contain("\"carrot_stage_1\"").And.NotContain("\r");
        using JsonDocument map = JsonDocument.Parse(json);
        JsonElement region = map.RootElement.GetProperty("sprites").GetProperty("carrot_stage_1");
        int x = region.GetProperty("x").GetInt32();
        int y = region.GetProperty("y").GetInt32();
        region.GetProperty("h").GetInt32().Should().Be(48);
        using SKBitmap atlas = AtlasBuilder.Decode(Path.Combine(Output, "crops.png"));
        atlas.GetPixel(x, y).Should().Be(new SKColor(20, 180, 40, 128));
        atlas.GetPixel(x + 31, y + 47).Should().Be(new SKColor(20, 180, 40, 128));
    }

    [Fact]
    public void Run_CheckAfterPack_ReportsCurrent()
    {
        WriteSprite("crops/wheat_stage_0.png", 32, 32, SKColors.Gold);
        Program.Run(Assets, Output, check: false, TextWriter.Null);

        int exit = Program.Run(Assets, Output, check: true, TextWriter.Null);

        exit.Should().Be(0);
    }

    [Fact]
    public void Run_CheckAfterSourceChanged_ReportsStale()
    {
        WriteSprite("crops/wheat_stage_0.png", 32, 32, SKColors.Gold);
        Program.Run(Assets, Output, check: false, TextWriter.Null);
        WriteSprite("crops/wheat_stage_0.png", 32, 32, SKColors.Brown);

        int exit = Program.Run(Assets, Output, check: true, TextWriter.Null);

        exit.Should().Be(1);
    }

    [Fact]
    public void Run_InvalidSpriteName_Throws()
    {
        WriteSprite("crops/Carrot Big.png", 32, 32, SKColors.Orange);

        Action act = () => Program.Run(Assets, Output, check: false, TextWriter.Null);

        act.Should().Throw<InvalidDataException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteSprite(string relativePath, int width, int height, SKColor color)
    {
        using SKBitmap bitmap = new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(color);
        using FileStream file = File.Create(Path.Combine(Assets, relativePath));
        bitmap.Encode(file, SKEncodedImageFormat.Png, 100);
    }
}

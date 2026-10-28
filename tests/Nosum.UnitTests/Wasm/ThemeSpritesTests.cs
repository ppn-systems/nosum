// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The sprite table of the UI theme (<c>nosum-theme.js</c>, ADR-016) only names rectangles that exist on the sheets it links.</summary>
public sealed partial class ThemeSpritesTests
{
    [Fact]
    public void SpriteTable_EveryRectangleLiesInsideItsSheet()
    {
        string root = RepositoryRoot();
        string script = File.ReadAllText(Path.Combine(root, "wasm", "Nosum.Wasm.Controls", "wwwroot", "nosum-theme.js"));
        Dictionary<string, string> sheets = SheetRegex().Matches(script).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        MatchCollection rows = RowRegex().Matches(script);

        sheets.Should().NotBeEmpty();
        rows.Should().NotBeEmpty();
        foreach (Match row in rows)
        {
            string sheet = row.Groups[1].Value;
            int x = Number(row, 2);
            int y = Number(row, 3);
            int w = Number(row, 4);
            int h = Number(row, 5);
            (int width, int height) = PngSize(Path.Combine(root, "assets", sheets[sheet].Replace('/', Path.DirectorySeparatorChar)));

            (x + w).Should().BeLessThanOrEqualTo(width, row.Value);
            (y + h).Should().BeLessThanOrEqualTo(height, row.Value);
        }
    }

    [Fact]
    public void Sheets_AreLinkedFromAssetsInTheControlsProject()
    {
        string root = RepositoryRoot();
        string project = File.ReadAllText(Path.Combine(root, "wasm", "Nosum.Wasm.Controls", "Nosum.Wasm.Controls.csproj"));

        foreach (string file in new[] { "ui\\menu.png", "ui\\cursors.png", "ui\\text_box.png" })
        {
            project.Should().Contain("..\\..\\assets\\" + file);
            File.Exists(Path.Combine(root, "assets", file.Replace('\\', Path.DirectorySeparatorChar))).Should().BeTrue();
        }
    }

    [Fact]
    public void RazorFiles_NeverUseRawHtmlTitleAttribute_UseDataTipInstead()
    {
        string wasmRoot = Path.Combine(RepositoryRoot(), "wasm");
        string[] razorFiles = Directory.GetFiles(wasmRoot, "*.razor", SearchOption.AllDirectories);

        razorFiles.Should().NotBeEmpty();
        foreach (string file in razorFiles)
        {
            string content = File.ReadAllText(file);
            content.Should().NotContain(" title=", $"{Path.GetFileName(file)} must use data-tip instead of raw HTML title attribute (blazor-ui)");
        }
    }

    [Fact]
    public void CssFiles_NeverUseFontWeightBold_SoPixelFontStaysCrisp()
    {
        string wasmRoot = Path.Combine(RepositoryRoot(), "wasm");
        string[] cssFiles = Directory.GetFiles(wasmRoot, "*.razor.css", SearchOption.AllDirectories);

        cssFiles.Should().NotBeEmpty();
        foreach (string file in cssFiles)
        {
            string content = File.ReadAllText(file);
            content.Should().NotContain("font-weight: bold", $"{Path.GetFileName(file)} must not use font-weight: bold on VT323 (blazor-ui)");
        }
    }

    [Fact]
    public void SkillsPanel_UsesSharedGameProgressControl_InsteadOfHandRolledBars()
    {
        string skillsPanel = File.ReadAllText(Path.Combine(RepositoryRoot(), "wasm", "Nosum.Wasm.Controls", "Progress", "SkillsPanel.razor"));

        skillsPanel.Should().Contain("<GameProgress");
        skillsPanel.Should().NotContain("class=\"skill-bar\"");
    }

    [Fact]
    public void KeyStrips_UseGameButtonChipVariant_AndHotbarSkillsAreCompactKeyCapsWithoutTextClutter()
    {
        string root = RepositoryRoot();
        string hotbar = File.ReadAllText(Path.Combine(root, "wasm", "Nosum.Wasm.Controls", "Inventory", "Hotbar.razor"));
        string hotbarCss = File.ReadAllText(Path.Combine(root, "wasm", "Nosum.Wasm.Controls", "Inventory", "Hotbar.razor.css"));
        string placement = File.ReadAllText(Path.Combine(root, "wasm", "Nosum.Wasm.Controls", "Placement", "PlacementPanel.razor"));

        hotbar.Should().Contain("Variant=\"chip\"");
        hotbar.Should().NotContain("Variant=\"plain\"");
        hotbar.Should().Contain("class=\"hotbar-skills\"");
        hotbar.Should().NotContain("<span>@Progress.WhirlwindLabel</span>");
        hotbarCss.Should().Contain(".hotbar-skills");
        placement.Should().Contain("Variant=\"chip\"");
        placement.Should().NotContain("<button");
    }

    [Fact]
    public void GameHud_PlayerBar_IsStackedVerticallyOnRightEdge_ToSaveScreenSpace()
    {
        string hudCss = File.ReadAllText(Path.Combine(RepositoryRoot(), "wasm", "Nosum.Wasm.Controls", "Layout", "GameHud.razor.css"));

        hudCss.Should().NotContain("grid-template-columns: repeat(3, max-content)");
        hudCss.Should().Contain(".hud-top-right ::deep .hud-player-bar");
    }

    [Fact]
    public void FarmerSprite_SupportsHeldItemsAndSwingAnimations_AndSheetHasActionPose()
    {
        string root = RepositoryRoot();
        string farmerSprite = File.ReadAllText(Path.Combine(root, "wasm", "Nosum.Wasm", "Game", "FarmerSprite.cs"));

        farmerSprite.Should().Contain("DrawHeldItem");
        farmerSprite.Should().Contain("DrawSkillEffect");
    }

    private static int Number(Match row, int group) => int.Parse(row.Groups[group].Value, System.Globalization.CultureInfo.InvariantCulture);

    private static (int Width, int Height) PngSize(string path)
    {
        // The PNG signature (8 bytes) and the IHDR chunk header (8 bytes) come first, then width and height as big-endian ints.
        byte[] header = new byte[24];
        using FileStream stream = File.OpenRead(path);
        stream.ReadExactly(header);
        return (BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20)));
    }

    private static string RepositoryRoot([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));

    [GeneratedRegex("""^\s{4}(\w+): "([^"]+)",""", RegexOptions.Multiline)]
    private static partial Regex SheetRegex();

    [GeneratedRegex("""\["[\w-]+", "(\w+)", (\d+), (\d+), (\d+), (\d+)\]""")]
    private static partial Regex RowRegex();
}

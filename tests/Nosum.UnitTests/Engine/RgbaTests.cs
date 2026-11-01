// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nosum.Rendering;
using Nosum.UnitTests.Maps;
using Xunit;

namespace Nosum.UnitTests.Engine;

/// <summary>
/// The colour helper and the guard against ARGB tint literals (docs/qa/combat-round5, PR #322: the cue was written in ARGB order and drew a
/// near-transparent colour). Every fill tint is <c>0xRRGGBBAA</c>; the scan keeps a raw literal from reaching a tint or fill line again.
/// </summary>
public sealed partial class RgbaTests
{
    // A tint or fill line: the word tint, or a call that takes one.
    [GeneratedRegex("tint|FillTexture|AddOverlay|FillRect", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TintContext();

    // A raw 8-digit hex literal, bare or wrapped in unchecked((int)...).
    [GeneratedRegex(@"unchecked\(\(int\)0x[0-9a-fA-F]{8}\)|(?<![\w.])0x[0-9a-fA-F]{8}\b", RegexOptions.CultureInvariant)]
    private static partial Regex RawLiteral();

    private static readonly string[] SourceRoots = ["wasm", "engine"];

    [Fact]
    public void From_RedIsTopByte_AlphaIsLowByte()
    {
        int amber = Rgba.From(0xFA, 0xCC, 0x15, 0xFF);

        amber.Should().Be(unchecked((int)0xfaCC15ff));
        ((amber >> 24) & 0xFF).Should().Be(0xFA, "red is the top byte, as renderer.js reads it");
        (amber & 0xFF).Should().Be(0xFF, "alpha is the low byte");
    }

    [Fact]
    public void From_DefaultAlpha_IsOpaque()
    {
        Rgba.From(0x1C, 0x19, 0x17).Should().Be(unchecked((int)0x1c1917ff));
    }

    [Fact]
    public void From_OutOfRangeChannels_AreClamped()
    {
        Rgba.From(300, -5, 128, 999).Should().Be(unchecked((int)0xff0080ff));
    }

    [Fact]
    public void WithAlpha_ReplacesOnlyTheLowByte()
    {
        Rgba.WithAlpha(unchecked((int)0xfacc15ff), 160).Should().Be(unchecked((int)0xfacc15a0));
    }

    [Fact]
    public void TintSources_NoRawLiteralOnTintOrFillLines()
    {
        List<string> offenders = [];
        foreach (string file in SourceFiles())
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Contains("Rgba.", System.StringComparison.Ordinal) || line.TrimStart().StartsWith("//", System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (TintContext().IsMatch(line) && RawLiteral().IsMatch(line))
                {
                    offenders.Add($"{Path.GetRelativePath(RepoFiles.Path(), file)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        offenders.Should().BeEmpty("write tints with Rgba.From(r, g, b, a), so the byte order lives in one place");
    }

    private static IEnumerable<string> SourceFiles() =>
        SourceRoots
            .Select(root => RepoFiles.Path(root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", System.StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", System.StringComparison.Ordinal)
                && Path.GetFileName(f) != "Rgba.cs");
}

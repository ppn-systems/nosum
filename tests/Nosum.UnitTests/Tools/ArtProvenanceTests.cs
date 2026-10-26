// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nosum.UnitTests.Maps;
using Xunit;

namespace Nosum.UnitTests.Tools;

/// <summary>
/// The provenance record of the art (docs/adr/ADR-042 D6): every tileset image a map names and every file under <c>assets/</c> is listed with its
/// source. Nosum's own work is listed in <c>art/own/SOURCES.md</c>; a CC0 pack, or a file drawn by <c>tools/art_repack</c>, in the Files table of
/// <c>docs/art/provenance.md</c>.
/// </summary>
public sealed partial class ArtProvenanceTests
{
    private static readonly string s_ownSources = File.ReadAllText(RepoFiles.Path("art", "own", "SOURCES.md"));
    private static readonly string s_provenance = File.ReadAllText(RepoFiles.Path("docs", "art", "provenance.md"));

    [Fact]
    public void Maps_EveryTilesetImage_IsListedInOwnSources()
    {
        List<string> images = TilesetImages();

        images.Should().NotBeEmpty();
        images.Should().OnlyContain(image => s_ownSources.Contains("`" + image + "`", StringComparison.Ordinal), "art/own/SOURCES.md must list the tileset image with its source");
    }

    [Fact]
    public void Maps_EveryTilesetImage_Exists()
    {
        string own = RepoFiles.Path("art", "own");

        TilesetImages().Should().OnlyContain(image => File.Exists(Path.Combine(own, image)), "a map names a tileset image that is not in art/own");
    }

    [Fact]
    public void Assets_EveryFileButReadme_IsListedWithItsSource()
    {
        string assets = RepoFiles.Path("assets");
        string[] files = Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(assets, file).Replace('\\', '/'))
            .Where(file => file != "README.md")
            .ToArray();
        Dictionary<string, string> sourceOf = FilesTable();

        files.Should().NotBeEmpty();
        foreach (string file in files)
        {
            bool listed = sourceOf.TryGetValue(file, out string? source) ? source.Length > 0 : s_ownSources.Contains("`" + file + "`", StringComparison.Ordinal);
            listed.Should().BeTrue(file + " is under assets/ but neither provenance.md (with a source) nor art/own/SOURCES.md lists it");
        }
    }

    // Every tileset image a .tmx under art/own/Maps names, relative to art/own (as SOURCES.md writes it: Maps/cave.png).
    private static List<string> TilesetImages()
    {
        string own = RepoFiles.Path("art", "own");
        string maps = Path.Combine(own, "Maps");

        return Directory.EnumerateFiles(maps, "*.tmx", SearchOption.AllDirectories)
            .SelectMany(map => ImagesOf(map).Select(source => Path.GetRelativePath(own, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(map)!, source))).Replace('\\', '/')))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(image => image, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<string> ImagesOf(string map) => ImageSource().Matches(File.ReadAllText(map)).Select(m => m.Groups[1].Value);

    // The "Files" table of provenance.md: file under assets/ to its Sources cell.
    private static Dictionary<string, string> FilesTable()
    {
        int start = s_provenance.IndexOf("## Files", StringComparison.Ordinal);
        int end = s_provenance.IndexOf("## Mapping", start, StringComparison.Ordinal);

        return Row().Matches(s_provenance[start..end]).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim(), StringComparer.Ordinal);
    }

    [GeneratedRegex("<image [^>]*source=\"([^\"]+)\"")]
    private static partial Regex ImageSource();

    [GeneratedRegex(@"^\| `([^`]+)` \|[^|\n]*\|([^|\n]*)\|", RegexOptions.Multiline)]
    private static partial Regex Row();
}

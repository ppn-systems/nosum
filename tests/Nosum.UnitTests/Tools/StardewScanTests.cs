// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Nosum.AssetInventory;
using Nosum.UnitTests.Maps;
using Xunit;

namespace Nosum.UnitTests.Tools;

/// <summary>
/// The Stardew Valley scan (docs/adr/ADR-042 D5): a file of the repository whose hash is one of the unpacked Stardew fingerprints is found
/// wherever it sits. The repository test records the files that still match as an allow-list. The list may only shrink: a new match fails it,
/// and so does an entry that no longer matches. The last step of ADR-042 deletes it.
/// </summary>
public sealed class StardewScanTests
{
    // The byte-identical copies of the unpacked Stardew maps that the game still hosts (ADR-042 D1). Each entry is removed in the commit that replaces its map.
    private static readonly string[] s_knownHits =
    [
        "art/own/Maps/Barn.tmx",
        "art/own/Maps/Coop.tmx",
        "art/own/Maps/FarmHouse.tmx",
        "art/own/Maps/Mine.tmx",
        "art/own/Maps/Mines/1.tmx",
        "art/own/Maps/Mines/10.tmx",
        "art/own/Maps/Mines/11.tmx",
        "art/own/Maps/Mines/12.tmx",
        "art/own/Maps/Mines/13.tmx",
        "art/own/Maps/Mines/14.tmx",
        "art/own/Maps/Mines/15.tmx",
        "art/own/Maps/Mines/16.tmx",
        "art/own/Maps/Mines/17.tmx",
        "art/own/Maps/Mines/18.tmx",
        "art/own/Maps/Mines/19.tmx",
        "art/own/Maps/Mines/2.tmx",
        "art/own/Maps/Mines/21.tmx",
        "art/own/Maps/Mines/22.tmx",
        "art/own/Maps/Mines/23.tmx",
        "art/own/Maps/Mines/24.tmx",
        "art/own/Maps/Mines/25.tmx",
        "art/own/Maps/Mines/26.tmx",
        "art/own/Maps/Mines/27.tmx",
        "art/own/Maps/Mines/28.tmx",
        "art/own/Maps/Mines/29.tmx",
        "art/own/Maps/Mines/3.tmx",
        "art/own/Maps/Mines/31.tmx",
        "art/own/Maps/Mines/32.tmx",
        "art/own/Maps/Mines/33.tmx",
        "art/own/Maps/Mines/34.tmx",
        "art/own/Maps/Mines/35.tmx",
        "art/own/Maps/Mines/36.tmx",
        "art/own/Maps/Mines/37.tmx",
        "art/own/Maps/Mines/38.tmx",
        "art/own/Maps/Mines/39.tmx",
        "art/own/Maps/Mines/4.tmx",
        "art/own/Maps/Mines/40.tmx",
        "art/own/Maps/Mines/41.tmx",
        "art/own/Maps/Mines/5.tmx",
        "art/own/Maps/Mines/6.tmx",
        "art/own/Maps/Mines/7.tmx",
        "art/own/Maps/Mines/8.tmx",
        "art/own/Maps/Mines/9.tmx",
    ];

    [Fact]
    public void Repository_StardewHits_AreExactlyTheKnownHits_AndTheListOnlyShrinks()
    {
        HashSet<string> fingerprints = StardewScan.ReadFingerprints(RepoFiles.Path("docs", "art", StardewScan.FileName));
        List<string> hits = StardewScan.Scan(RepoFiles.Path(), fingerprints);

        hits.Should().BeEquivalentTo(
            s_knownHits,
            "a file whose hash is a Stardew fingerprint is Stardew content (docs/adr/ADR-042 D5): replace it; a known map that was replaced leaves the allow-list in the same commit");
    }

    [Fact]
    public void Scan_ListedHash_IsFoundAtAnyDepth_WithForwardSlashes()
    {
        string root = Directory.CreateTempSubdirectory("nosum-stardew-").FullName;
        try
        {
            WriteFile(root, ["a", "b", "copy.bin"], "stardew");

            StardewScan.Scan(root, new HashSet<string> { Hash("stardew") }).Should().Equal("a/b/copy.bin");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_BuildOutputAndGitData_AreSkipped()
    {
        string root = Directory.CreateTempSubdirectory("nosum-stardew-").FullName;
        try
        {
            foreach (string folder in new[] { ".git", "bin", "obj", "artifacts", "node_modules" })
            {
                WriteFile(root, [folder, "copy.bin"], "stardew");
            }

            StardewScan.Scan(root, new HashSet<string> { Hash("stardew") }).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_EmptyFile_NeverMatches_EvenWhenTheEmptyHashIsListed()
    {
        string root = Directory.CreateTempSubdirectory("nosum-stardew-").FullName;
        try
        {
            WriteFile(root, [".gitkeep"], string.Empty);

            StardewScan.Scan(root, new HashSet<string> { Hash(string.Empty) }).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadFingerprints_SkipsCommentsAndBlankLines_AndRefusesAnythingButLowerCaseHex()
    {
        string root = Directory.CreateTempSubdirectory("nosum-stardew-").FullName;
        try
        {
            string list = Path.Combine(root, "list.sha256");
            File.WriteAllText(list, "# a comment\n\n" + Hash("stardew") + "\n");
            StardewScan.ReadFingerprints(list).Should().ContainSingle().Which.Should().Be(Hash("stardew"));

            File.WriteAllText(list, Hash("stardew").ToUpperInvariant() + "\n");
            Action read = () => StardewScan.ReadFingerprints(list);
            read.Should().Throw<InvalidDataException>().WithMessage("*list.sha256:1*");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StardewScanMode_ExitsZeroWhenClean_OneWhenAFileMatches_TwoWhenTheListIsMissing()
    {
        string root = Directory.CreateTempSubdirectory("nosum-stardew-").FullName;
        try
        {
            using StringWriter missing = new();
            Program.StardewScanMode(root, missing).Should().Be(2, "the list is missing");

            _ = Directory.CreateDirectory(Path.Combine(root, "docs", "art"));
            File.WriteAllText(Path.Combine(root, "docs", "art", StardewScan.FileName), Hash("stardew") + "\n");
            using StringWriter clean = new();
            Program.StardewScanMode(root, clean).Should().Be(0, "nothing in the tree matches");

            WriteFile(root, ["wasm", "copy.bin"], "stardew");
            using StringWriter found = new();
            Program.StardewScanMode(root, found).Should().Be(1);
            found.ToString().Should().Contain("STARDEW       wasm/copy.bin").And.Contain("1 file(s) are Stardew Valley content.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteFile(string root, string[] parts, string content)
    {
        string folder = Path.Combine([root, .. parts[..^1]]);
        _ = Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, parts[^1]), content);
    }

    private static string Hash(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nosum.AssetInventory;
using Nosum.UnitTests.Maps;
using Xunit;

namespace Nosum.UnitTests.Tools;

/// <summary>
/// <c>docs/art/provenance.md</c> (docs/adr/ADR-004, ADR-040) records the source of every replaced art file. These tests fail when a file it lists is
/// missing, is still the placeholder, or names a pack whose license text is not kept next to the art.
/// </summary>
public sealed partial class ProvenanceTests
{
    private static readonly string s_provenance = File.ReadAllText(RepoFiles.Path("docs", "art", "provenance.md"));

    [Fact]
    public void Files_EveryListedFile_ExistsAndIsNoLongerThePlaceholder()
    {
        List<string> listed = FilesSection().Select(m => m.Groups[1].Value).ToList();
        Fingerprints recorded = Fingerprints.Read(RepoFiles.Path("docs", "art", Fingerprints.FileName));
        string assets = RepoFiles.Path("assets");

        listed.Should().NotBeEmpty();
        foreach (string path in listed)
        {
            File.Exists(Path.Combine(assets, path)).Should().BeTrue(path + " is listed in provenance.md but missing");
            recorded.StateOf(path, assets).Should().Be(ArtState.Replaced, path + " is listed in provenance.md, so it must differ from its placeholder fingerprint and keep its size");
        }
    }

    [Fact]
    public void Files_EveryListedFile_IsAFileTheGameLoads()
    {
        if (!PlaceholderContent.Available)
        {
            return;
        }

        HashSet<string> loaded = [.. Inventory.Build(RepoFiles.Path(), PlaceholderContent.Assets).Listed.Select(f => f.Path)];

        FilesSection().Select(m => m.Groups[1].Value).Should().OnlyContain(path => loaded.Contains(path), "provenance.md lists a file the inventory does not know");
    }

    [Fact]
    public void Sources_EveryPack_HasItsLicenseTextKept()
    {
        List<string> packs = SourcesSection().Select(m => m.Groups[1].Value).Where(name => !name.Contains('/', System.StringComparison.Ordinal)).ToList();

        packs.Should().NotBeEmpty();
        foreach (string pack in packs)
        {
            File.Exists(RepoFiles.Path("docs", "art", "licenses", pack + ".txt")).Should().BeTrue("the license of " + pack + " must be committed next to its provenance");
        }
    }

    private static IEnumerable<Match> FilesSection() => Section("## Files", "## Mapping");

    private static IEnumerable<Match> SourcesSection() => Section("## Sources", "## Files");

    private static IEnumerable<Match> Section(string from, string to)
    {
        int start = s_provenance.IndexOf(from, System.StringComparison.Ordinal);
        int end = s_provenance.IndexOf(to, start, System.StringComparison.Ordinal);
        return Row().Matches(s_provenance[start..end]);
    }

    [GeneratedRegex(@"^\| `([^`]+)` \|", RegexOptions.Multiline)]
    private static partial Regex Row();
}

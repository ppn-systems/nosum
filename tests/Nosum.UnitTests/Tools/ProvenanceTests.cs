// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nosum.UnitTests.Maps;
using Nosum.Wasm.ViewModels.Settings;
using Xunit;

namespace Nosum.UnitTests.Tools;

/// <summary>
/// <c>docs/art/provenance.md</c> (docs/adr/ADR-004) records the source of the third-party art. These tests fail when a pack it names has no license text
/// kept next to the art.
/// </summary>
public sealed partial class ProvenanceTests
{
    private static readonly string s_provenance = File.ReadAllText(RepoFiles.Path("docs", "art", "provenance.md"));

    [Fact]
    public void Sources_EveryPack_HasItsLicenseTextKept()
    {
        List<string> packs = SourcesSection().Select(m => m.Groups[1].Value).Where(name => !name.Contains('/', StringComparison.Ordinal)).ToList();

        packs.Should().NotBeEmpty();
        foreach (string pack in packs)
        {
            File.Exists(RepoFiles.Path("docs", "art", "licenses", pack + ".txt")).Should().BeTrue("the license of " + pack + " must be committed next to its provenance");
        }
    }

    [Fact]
    public void Credits_EveryCcByPack_IsInTheCreditsPlayersSee()
    {
        string sources = s_provenance[s_provenance.IndexOf("## Sources", StringComparison.Ordinal)..s_provenance.IndexOf("## Credits", StringComparison.Ordinal)];
        List<string> ccBy = CcByRow().Matches(sources).Select(m => m.Groups[1].Value).ToList();

        ccBy.Should().NotBeEmpty();
        foreach (string pack in ccBy)
        {
            ArtCredits.All.Should().Contain(credit => credit.Pack == pack && credit.License.StartsWith("CC-BY", StringComparison.Ordinal), pack + " is CC-BY and must be in the credits shown to players");
        }
    }

    [Fact]
    public void Credits_EveryPackOfTheSourcesTable_IsListed()
    {
        List<string> packs = SourcesSection().Select(m => m.Groups[1].Value).Where(name => !name.Contains('/', StringComparison.Ordinal)).ToList();

        ArtCredits.All.Select(credit => credit.Pack).Should().BeEquivalentTo(packs);
    }

    private static IEnumerable<Match> SourcesSection() => Section("## Sources", "## Files");

    private static IEnumerable<Match> Section(string from, string to)
    {
        int start = s_provenance.IndexOf(from, StringComparison.Ordinal);
        int end = s_provenance.IndexOf(to, start, StringComparison.Ordinal);
        return Row().Matches(s_provenance[start..end]);
    }

    [GeneratedRegex(@"^\| `([^`/]+)` \|[^\n]*CC-BY", RegexOptions.Multiline)]
    private static partial Regex CcByRow();

    [GeneratedRegex(@"^\| `([^`]+)` \|", RegexOptions.Multiline)]
    private static partial Regex Row();
}

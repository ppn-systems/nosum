// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nosum.UnitTests.Maps;
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
        List<string> packs = SourcesSection().Select(m => m.Groups[1].Value).Where(name => !name.Contains('/', System.StringComparison.Ordinal)).ToList();

        packs.Should().NotBeEmpty();
        foreach (string pack in packs)
        {
            File.Exists(RepoFiles.Path("docs", "art", "licenses", pack + ".txt")).Should().BeTrue("the license of " + pack + " must be committed next to its provenance");
        }
    }

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

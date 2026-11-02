// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nosum.Contracts;
using Xunit;

namespace Nosum.UnitTests.Contracts;

/// <summary>
/// Guards the history in the <c>NosumProtocol.Version</c> doc comment: each version is documented once, and the constant is the highest one.
/// A merge that adds a version line without bumping the constant, or bumps it twice, fails here.
/// </summary>
public sealed partial class NosumProtocolVersionDocTests
{
    [Fact]
    public void Version_DocumentedLines_AreUnique()
    {
        List<int> documented = DocumentedVersions();

        List<int> duplicates = documented.GroupBy(version => version).Where(group => group.Count() > 1).Select(group => group.Key).ToList();

        duplicates.Should().BeEmpty("each protocol version must be documented on exactly one line of NosumProtocol.Version");
    }

    [Fact]
    public void Version_IsTheHighestDocumentedVersion()
    {
        List<int> documented = DocumentedVersions();

        documented.Should().NotBeEmpty("the Version doc comment lists the protocol history");
        documented.Max().Should().Be(NosumProtocol.Version, "the constant must equal the highest version in its doc comment");
    }

    private static List<int> DocumentedVersions()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "contracts", "Nosum.Contracts", "NosumProtocol.cs"));
        return source.Split('\n')
            .Select(line => DocVersionLine().Match(line))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
    }

    [GeneratedRegex(@"^\s*///\s*(\d+):\s", RegexOptions.CultureInvariant)]
    private static partial Regex DocVersionLine();

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}

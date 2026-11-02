// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Nosum.UnitTests.Docs;

/// <summary>
/// Guards the architecture decision records: every <c>docs/adr/ADR-NNN-*.md</c> file owns its number, so two merges cannot both claim one.
/// </summary>
public sealed partial class AdrNumberingTests
{
    [Fact]
    public void AdrFiles_EachNumber_IsUsedByOneFileOnly()
    {
        string adrDirectory = Path.Combine(RepoRoot(), "docs", "adr");
        List<string> duplicates = Directory.EnumerateFiles(adrDirectory, "ADR-*.md")
            .Select(Path.GetFileName)
            .Where(name => name is not null && AdrFileName().IsMatch(name))
            .GroupBy(name => int.Parse(AdrFileName().Match(name!).Groups[1].Value, CultureInfo.InvariantCulture))
            .Where(group => group.Count() > 1)
            .Select(group => $"ADR-{group.Key:D3}: {string.Join(", ", group.OrderBy(name => name, StringComparer.Ordinal))}")
            .ToList();

        duplicates.Should().BeEmpty("each ADR number must belong to exactly one file in docs/adr");
    }

    [GeneratedRegex(@"^ADR-(\d+)-", RegexOptions.CultureInvariant)]
    private static partial Regex AdrFileName();

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

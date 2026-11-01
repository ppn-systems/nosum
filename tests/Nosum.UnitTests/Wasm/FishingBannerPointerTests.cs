// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>
/// The fishing banner sits over the farm pond. It shows the line's state and takes no clicks, so a click on the water under it reaches the
/// canvas: a cast, and the press that reels the line in (docs/adr/ADR-078 D9). A banner that takes the click swallows both.
/// </summary>
public sealed class FishingBannerPointerTests
{
    [Fact]
    public void FishingBannerStyles_BannerAndItsPanel_PassClicksThroughToTheWorld()
    {
        string css = File.ReadAllText(Path.Combine(RepositoryRoot(), "wasm", "Nosum.Wasm.Controls", "Fishing", "FishingBanner.razor.css"));

        DeclarationsOf(css, ".fishing-status").Should().Contain("pointer-events: none");
    }

    [Fact]
    public void FishingBannerStyles_CatchCard_StillPassesClicksThrough()
    {
        string css = File.ReadAllText(Path.Combine(RepositoryRoot(), "wasm", "Nosum.Wasm.Controls", "Fishing", "FishingCatchCard.razor.css"));

        DeclarationsOf(css, ".fishing-card").Should().Contain("pointer-events: none");
    }

    // The declarations of the one rule whose selector is exactly the given text; empty when there is no such rule.
    private static string DeclarationsOf(string css, string selector)
    {
        Match match = Regex.Match(css, "(?m)^" + Regex.Escape(selector) + @"\s*\{(?<body>[^}]*)\}");
        return match.Success ? match.Groups["body"].Value : string.Empty;
    }

    private static string RepositoryRoot([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
}

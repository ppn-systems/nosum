// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Wasm.ViewModels.Minimap;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>
/// The minimap frame: the card while the map is drawn, one map button only when the player hid the map (Settings, or a phone by default), and
/// nothing after an M-key hide (issue 3 of the QA pass 5; docs/design/ui-standard.md section 6).
/// </summary>
[Collection(SharedUiCulture.Name)]
public sealed class MinimapVisibilityTests
{
    private static MinimapViewModel Frame()
        => new(new StringLocalizer<Language>(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance)));

    [Fact]
    public void MapDrawn_ShowsTheCardAndNoButton()
    {
        MinimapViewModel frame = Frame();
        frame.Shown = true;

        frame.Drawn.Should().BeTrue();
        frame.ShowsButton.Should().BeFalse();
    }

    [Fact]
    public void MAfterHide_ShowsNeitherCardNorButton()
    {
        MinimapViewModel frame = Frame();
        frame.Shown = true;
        frame.Shown = false;

        frame.Drawn.Should().BeFalse();
        frame.ShowsButton.Should().BeFalse("an M-key hide must leave no minimap, not a smaller one");
    }

    [Fact]
    public void HiddenBySettings_ShowsOnlyTheMapButton()
    {
        MinimapViewModel frame = Frame();
        frame.Shown = true;
        frame.Hidden = true;

        frame.Drawn.Should().BeFalse();
        frame.ShowsButton.Should().BeTrue("the world map stays in reach through one button");
    }

    [Fact]
    public void CoveredByAPanel_KeepsTheCardDrawnForTheScreenWidthToDecide()
    {
        // A window is narrower than the corner's breakpoint hides the card by CSS (HudMinimap.razor.css) and the map by the canvas (MapScene.Minimap);
        // from 1600 px it stays beside the window. The view model still draws the card and reports the cover.
        MinimapViewModel frame = Frame();
        frame.Shown = true;
        frame.Covered = true;

        frame.Drawn.Should().BeTrue();
        frame.Covered.Should().BeTrue();
        frame.ShowsButton.Should().BeFalse("a shown map has no button, covered or not");
    }

    [Fact]
    public void HiddenBySettingsAndCovered_ShowsTheButtonOnly()
    {
        MinimapViewModel frame = Frame();
        frame.Shown = true;
        frame.Hidden = true;
        frame.Covered = true;

        frame.Drawn.Should().BeFalse();
        frame.ShowsButton.Should().BeTrue("the player hid the map: its one button stays in the column's place");
    }
}

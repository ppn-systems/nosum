// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Wasm.ViewModels;
using Nosum.Wasm.ViewModels.Minimap;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.ViewModels.Settings;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The minimap card can be hidden in Settings &gt; Display, remembered per player; a phone starts with it hidden (docs/adr/ADR-085 D5).</summary>
[Collection(SharedUiCulture.Name)]
public sealed class MinimapSettingTests
{
    private sealed class MemoryStore : ISettingsStore
    {
        public string? Text { get; set; }

        public string? Read() => this.Text;

        public void Write(string text) => this.Text = text;
    }

    private static StringLocalizer<Language> Text()
        => new(new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance));

    [Fact]
    public void Minimap_NoChoice_IsHiddenOnlyOnANarrowScreen()
    {
        using SettingsViewModel settings = new(Text(), new MemoryStore());

        settings.MinimapHiddenNow.Should().BeFalse("a desktop shows the minimap");

        settings.SetNarrowScreen(true);

        settings.MinimapHiddenNow.Should().BeTrue("a phone starts with the minimap hidden");
    }

    [Fact]
    public void Minimap_ChoiceToHide_IsStoredAndKeptOnTheNextVisit()
    {
        MemoryStore store = new();
        using SettingsViewModel settings = new(Text(), store);

        settings.SetMinimapHidden(true);

        settings.MinimapHiddenNow.Should().BeTrue();
        store.Text.Should().Contain("minimap=1\n");
        using SettingsViewModel next = new(Text(), store);
        next.MinimapHiddenNow.Should().BeTrue("the choice is remembered per player");
    }

    [Fact]
    public void Minimap_ChoiceToShowOnAPhone_WinsOverTheNarrowDefault()
    {
        MemoryStore store = new();
        using SettingsViewModel settings = new(Text(), store);
        settings.SetNarrowScreen(true);

        settings.SetMinimapHidden(false);

        settings.MinimapHiddenNow.Should().BeFalse();
        store.Text.Should().Contain("minimap=0\n");
    }

    [Fact]
    public void Minimap_SettingRoundTrips_AutoIsNull()
    {
        ClientSettings.Parse("minimap=auto").MinimapHidden.Should().BeNull();
        ClientSettings.Parse("minimap=1").MinimapHidden.Should().BeTrue();
        ClientSettings.Parse("minimap=0").MinimapHidden.Should().BeFalse();
        ClientSettings.Parse(new ClientSettings { MinimapHidden = null }.Serialize()).MinimapHidden.Should().BeNull();
        ClientSettings.Parse(new ClientSettings { MinimapHidden = true }.Serialize()).MinimapHidden.Should().BeTrue();
    }

    [Fact]
    public void Minimap_HiddenInSettings_DrawsNothingWhateverTheMapKeySays()
    {
        MinimapViewModel frame = new(Text());
        frame.Shown = true;

        frame.Drawn.Should().BeTrue();

        frame.Hidden = true;

        frame.Drawn.Should().BeFalse("a hidden card and its map stay away");

        frame.Hidden = false;

        frame.Drawn.Should().BeTrue();
    }
}

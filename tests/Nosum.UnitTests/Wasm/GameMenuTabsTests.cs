// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Gameplay.Items;
using Nosum.Wasm.ViewModels;
using Nosum.Wasm.ViewModels.Placement;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class GameMenuTabsTests
{
    private static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    [Fact]
    public void Items_EveryTabHasKindLabelIconAndAUniqueKind()
    {
        ToolbarViewModel vm = new(Localizer<Language>());

        vm.Items.Should().OnlyContain(i => i.Kind != PanelKind.None && i.Label.Length > 0 && !i.Label.StartsWith("Toolbar_", System.StringComparison.Ordinal) && i.Icon.StartsWith("tab-", System.StringComparison.Ordinal));
        vm.Items.Select(i => i.Kind).Should().OnlyHaveUniqueItems();
        vm.Items.Select(i => i.Icon).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Items_SettingsIsTheLastTabAndHOpensIt_ShopIsNotAMenuButton()
    {
        ToolbarViewModel vm = new(Localizer<Language>());

        vm.Items[^1].Kind.Should().Be(PanelKind.Settings);
        vm.Items[^1].Hotkey.Should().Be("H");
        vm.Items.Should().NotContain(i => i.Kind == PanelKind.Shop || i.Kind == PanelKind.Help);
        vm.ShopItem.Kind.Should().Be(PanelKind.Shop);
        vm.ShopItem.Label.Should().NotBeNullOrWhiteSpace().And.NotStartWith("Toolbar_");
    }

    [Fact]
    public void StripLabels_AreOneWordAndTheMoveKeyBecomesCancelWhileCarrying()
    {
        PlacementViewModel vm = new(Localizer<Catalogue>(), Localizer<Language>());

        string move = vm.StripMoveLabel;
        vm.SetCarrying(ItemCatalog.OakChair);

        vm.StripMoveLabel.Should().NotBe(move);
        new[] { vm.StripPlaceLabel, vm.StripRotateLabel, vm.StripMoveLabel, vm.StripStowLabel }
            .Should().OnlyContain(t => t.Length > 0 && !t.StartsWith("Placement_", System.StringComparison.Ordinal) && !t.Contains(' ', System.StringComparison.Ordinal));
    }
}

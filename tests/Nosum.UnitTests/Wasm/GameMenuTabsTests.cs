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
    public void Items_SettingsIsTheLastTabAndHOpensIt()
    {
        ToolbarViewModel vm = new(Localizer<Language>());

        vm.Items[^1].Kind.Should().Be(PanelKind.Settings);
        vm.Items[^1].Hotkey.Should().Be("H");
    }

    [Fact]
    public void Items_HoldOnlyPlayerThings_NoShopMarketBoardChestOrFurnaceTab()
    {
        ToolbarViewModel vm = new(Localizer<Language>());

        vm.Items.Select(i => i.Kind).Should().BeEquivalentTo(
        [
            PanelKind.Inventory, PanelKind.Wardrobe, PanelKind.Crafting, PanelKind.Trade, PanelKind.Coop, PanelKind.Visitors,
            PanelKind.Friends, PanelKind.Mail, PanelKind.Skills, PanelKind.Profile, PanelKind.Settings,
        ]);
        vm.Items.Select(i => i.Kind).Should().NotContain([PanelKind.Shop, PanelKind.Market, PanelKind.Goals, PanelKind.Chest, PanelKind.Furnace, PanelKind.Help]);
        vm.Items.Should().NotContain(i => i.Hotkey == "M", "the market opens at the trader, not with a key");
        ((int)PanelKind.Market).Should().Be(2, "panel kinds are append-only");
    }

    [Fact]
    public void Items_MailIsATabOfItsOwn()
    {
        ToolbarViewModel vm = new(Localizer<Language>());

        ToolbarItem mail = vm.Items.Single(i => i.Kind == PanelKind.Mail);

        mail.Icon.Should().Be("tab-mail");
        mail.Label.Should().NotBeNullOrWhiteSpace().And.NotStartWith("Toolbar_");
    }

    [Fact]
    public void MailTip_ShowsTheUnreadCountOnlyWhenThereIsUnreadMail_AndTheCogTipIsOnlySettings()
    {
        ToolbarViewModel vm = new(Localizer<Language>());

        vm.MailTip(0).Should().Be(vm.Text("Toolbar_Mail"));
        vm.MailTip(3).Should().StartWith(vm.Text("Toolbar_Mail")).And.Contain("3");
        vm.SettingsTip.Should().Be(vm.Text("Toolbar_Settings"));
        vm.SettingsTip.Should().NotContain(vm.Text("Toolbar_Mail"), "the cog holds settings only (docs/adr/ADR-043 D4)");
    }

    [Fact]
    public void TitleOf_ReturnsLocalizedTitleForEveryToolbarItem_AndEmptyForNonToolbarKind()
    {
        ToolbarViewModel vm = new(Localizer<Language>());

        foreach (ToolbarItem item in vm.Items)
        {
            vm.TitleOf(item.Kind).Should().Be(item.Label);
        }

        vm.TitleOf(PanelKind.None).Should().BeEmpty();
        vm.TitleOf(PanelKind.Shop).Should().BeEmpty();
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

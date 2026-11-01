// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Wasm.ViewModels;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class HudMenuColumnTests
{
    private static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    [Fact]
    public void ColumnItems_WithTheMenuButtonAtMostEight()
    {
        using ToolbarViewModel vm = new(Localizer<Language>());

        // The Menu button is the eighth at most (docs/design/ui-standard.md section 2).
        vm.ColumnItems.Count.Should().BeLessThan(8);
    }

    [Fact]
    public void ColumnItems_AreTabsOfTheToolbarInTheirOrder()
    {
        using ToolbarViewModel vm = new(Localizer<Language>());

        vm.ColumnItems.Select(i => i.Kind).Should().Equal(ToolbarViewModel.ColumnKinds);
        vm.ColumnItems.Should().OnlyContain(i => vm.Items.Contains(i));
    }

    [Fact]
    public void MenuItems_HoldEveryTabThatIsNotInTheColumn()
    {
        using ToolbarViewModel vm = new(Localizer<Language>());

        vm.MenuItems.Select(i => i.Kind).Should().BeEquivalentTo(
            vm.Items.Select(i => i.Kind).Where(k => !ToolbarViewModel.ColumnKinds.Contains(k) && !ToolbarViewModel.NpcKinds.Contains(k) && k is not (PanelKind.Mail or PanelKind.Settings)));
    }

    [Fact]
    public void NpcKinds_AreNeitherInTheColumnNorInTheMenu_TheirMerchantIsTheOnlyDoor()
    {
        using ToolbarViewModel vm = new(Localizer<Language>());

        ToolbarViewModel.NpcKinds.Should().Equal(PanelKind.Wardrobe, PanelKind.Coop);
        vm.ColumnItems.Select(i => i.Kind).Should().NotIntersectWith(ToolbarViewModel.NpcKinds);
        vm.MenuItems.Select(i => i.Kind).Should().NotIntersectWith(ToolbarViewModel.NpcKinds);
    }

    [Fact]
    public void Column_HoldsTheOneClickPanels_InferredFromTheDecisionTable()
    {
        ToolbarViewModel.ColumnKinds.Should().Equal(
            PanelKind.Inventory, PanelKind.Crafting, PanelKind.Trade, PanelKind.Friends, PanelKind.Skills, PanelKind.WorldMap, PanelKind.Journal);
    }

    [Fact]
    public void ColumnAndMenuItems_NeverShareAPanel_AndMailAndSettingsAreInNeither()
    {
        using ToolbarViewModel vm = new(Localizer<Language>());

        vm.ColumnItems.Select(i => i.Kind).Intersect(vm.MenuItems.Select(i => i.Kind)).Should().BeEmpty();
        vm.ColumnItems.Concat(vm.MenuItems).Select(i => i.Kind).Should().NotContain([PanelKind.Mail, PanelKind.Settings]);
    }

    [Fact]
    public void ColumnCollapsed_IsOffByDefault_SoADesktopMenuKeepsOnlyTheExtras()
    {
        using ToolbarViewModel vm = new(Localizer<Language>());

        vm.ColumnCollapsed.Should().BeFalse();
        vm.MenuItems.Select(i => i.Kind).Should().NotIntersectWith(ToolbarViewModel.ColumnKinds);
    }

    [Fact]
    public void PhoneMenu_HasATileForEveryColumnPanel()
    {
        using ToolbarViewModel vm = new(Localizer<Language>());
        vm.ColumnCollapsed = true;

        // The column is collapsed into the Menu button on a phone, so the Menu window is the only door to these panels (docs/design/ui-standard.md section 6).
        vm.MenuItems.Select(i => i.Kind).Should().Contain(ToolbarViewModel.ColumnKinds);
        vm.ColumnItems.Select(i => i.Kind).Should().Equal(ToolbarViewModel.ColumnKinds);
    }

    [Fact]
    public void PhoneMenu_KeepsTheExtrasAndStillLeavesOutMerchantsMailAndSettings()
    {
        using ToolbarViewModel vm = new(Localizer<Language>());
        vm.ColumnCollapsed = true;

        vm.MenuItems.Select(i => i.Kind).Should().BeEquivalentTo(
            vm.Items.Select(i => i.Kind).Where(k => !ToolbarViewModel.NpcKinds.Contains(k) && k is not (PanelKind.Mail or PanelKind.Settings)));
        vm.MenuItems.Select(i => i.Kind).Should().NotIntersectWith(ToolbarViewModel.NpcKinds);
    }

    [Fact]
    public void PhoneMenu_FitsTheTileGrid_TenTilesAtMost()
    {
        using ToolbarViewModel vm = new(Localizer<Language>());
        vm.ColumnCollapsed = true;

        // Ten tiles fill the 390x844 grid without scrolling (the QA pass checks the rendered window).
        vm.MenuItems.Count.Should().BeLessThanOrEqualTo(10);
    }

    [Fact]
    public void ColumnCollapsed_ChangingItRaisesChanged()
    {
        using ToolbarViewModel vm = new(Localizer<Language>());
        int raised = 0;
        vm.Changed += () => raised++;

        vm.ColumnCollapsed = true;
        vm.ColumnCollapsed = true;

        raised.Should().Be(1, "a value that does not change raises nothing");
    }

    [Fact]
    public void PanelKindMenu_IsAppendOnlyAtTheEnd()
    {
        ((int)PanelKind.Menu).Should().Be(21, "panel kinds are append-only");
    }
}

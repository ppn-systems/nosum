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
            vm.Items.Select(i => i.Kind).Where(k => !ToolbarViewModel.ColumnKinds.Contains(k) && k is not (PanelKind.Mail or PanelKind.Settings)));
    }

    [Fact]
    public void ColumnAndMenuItems_NeverShareAPanel_AndMailAndSettingsAreInNeither()
    {
        using ToolbarViewModel vm = new(Localizer<Language>());

        vm.ColumnItems.Select(i => i.Kind).Intersect(vm.MenuItems.Select(i => i.Kind)).Should().BeEmpty();
        vm.ColumnItems.Concat(vm.MenuItems).Select(i => i.Kind).Should().NotContain([PanelKind.Mail, PanelKind.Settings]);
    }

    [Fact]
    public void PanelKindMenu_IsAppendOnlyAtTheEnd()
    {
        ((int)PanelKind.Menu).Should().Be(21, "panel kinds are append-only");
    }
}

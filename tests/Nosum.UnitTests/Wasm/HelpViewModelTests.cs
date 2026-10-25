// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Wasm.ViewModels.Help;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class HelpViewModelTests
{
    [Fact]
    public void Sections_EveryKeyAndDescriptionHasText()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        HelpViewModel vm = new(new StringLocalizer<Language>(factory));

        HelpEntry[] entries = vm.Sections.SelectMany(s => s.Entries).ToArray();

        vm.Title.Should().NotBeNullOrWhiteSpace().And.NotStartWith("Help_");
        vm.Sections.Should().OnlyContain(s => s.Title.Length > 0 && !s.Title.StartsWith("Help_", System.StringComparison.Ordinal));
        entries.Should().NotBeEmpty();
        entries.Should().OnlyContain(e => e.Keys.Count > 0 && e.Description.Length > 0 && !e.Description.StartsWith("Help_", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Sections_ListEveryPanelAndFurnitureKey()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        HelpViewModel vm = new(new StringLocalizer<Language>(factory));

        string[] keys = vm.Sections.SelectMany(s => s.Entries).SelectMany(e => e.Keys).ToArray();

        keys.Should().Contain(["I", "C", "T", "G", "H", "Esc", "E", "R", "F", "Q", "1-9"]);
    }
}

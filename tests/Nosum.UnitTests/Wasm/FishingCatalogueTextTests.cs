// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using FluentAssertions;
using Nosum.Gameplay.Items;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The fishing rod, the fish stew and the ten fish have a name and a description in both languages, under their item keys (docs/adr/ADR-071 D3, i18n).</summary>
public sealed class FishingCatalogueTextTests
{
    [Fact]
    public void Catalogue_FishingRodStewAndFish_HaveNameAndDescriptionInBothLanguages()
    {
        int[] items =
        [
            ItemCatalog.FishingRod,
            ItemCatalog.FishStew,
            ItemCatalog.Anchovy,
            ItemCatalog.SeaBass,
            ItemCatalog.Mackerel,
            ItemCatalog.Pufferfish,
            ItemCatalog.Carp,
            ItemCatalog.Trout,
            ItemCatalog.Bass,
            ItemCatalog.Pike,
            ItemCatalog.Eel,
            ItemCatalog.GoldenKoi,
        ];
        List<string> needed = [];
        foreach (int id in items)
        {
            ItemCatalog.TryGet(id, out ItemDefinition? item).Should().BeTrue();
            needed.Add("Item_" + item!.Key + "_Name");
            needed.Add("Item_" + item.Key + "_Description");
        }

        Keys("Catalogue.resx").Should().Contain(needed);
        Keys("Catalogue.en.resx").Should().Contain(needed);
    }

    private static HashSet<string> Keys(string file, [CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
        string path = Path.Combine(root, "wasm", "Nosum.Wasm.ViewModels", "Resources", file);
        return XDocument.Load(path).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
    }
}

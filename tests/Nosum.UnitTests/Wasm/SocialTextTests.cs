// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using FluentAssertions;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Look;
using Nosum.Contracts.Market;
using Nosum.Contracts.Trade;
using Nosum.Gameplay.Items;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>Every UI string of the social layer exists in both languages, and every outcome code the server can send has a text.</summary>
public sealed class SocialTextTests
{
    [Fact]
    public void Language_BothFilesCarryTheSameKeys()
    {
        Keys("Language.resx").Should().BeEquivalentTo(Keys("Language.en.resx"));
    }

    [Fact]
    public void Language_EveryOutcomeCodeHasATextInBothLanguages()
    {
        List<string> needed = [];
        needed.AddRange(Enum.GetValues<MarketStatus>().Where(s => s != MarketStatus.Ok).Select(s => "Market_Status_" + s));
        needed.AddRange(Enum.GetValues<LookStatus>().Select(s => "Wardrobe_Status_" + s));
        needed.AddRange(Enum.GetValues<TradeStatus>().Select(s => "Trade_Status_" + s));
        needed.AddRange(Enum.GetValues<TradeEndReason>().Select(s => "Trade_Ended_" + s));
        needed.AddRange(Enum.GetValues<CoopStatus>().Select(s => "Coop_Status_" + s));
        needed.AddRange(Enum.GetValues<CoopRole>().Select(s => "Coop_Role_" + s));

        Keys("Language.resx").Should().Contain(needed);
        Keys("Language.en.resx").Should().Contain(needed);
    }

    [Fact]
    public void Catalogue_EveryClothingItemHasNameAndDescriptionInBothLanguages()
    {
        string[] needed = ItemCatalog.All.Where(i => i.Kind == ItemKind.Clothing).SelectMany(i => new[] { $"Item_{i.Key}_Name", $"Item_{i.Key}_Description" }).ToArray();

        needed.Should().NotBeEmpty();
        Keys("Catalogue.resx").Should().Contain(needed);
        Keys("Catalogue.en.resx").Should().Contain(needed);
    }

    [Fact]
    public void WearSlot_ValuesAreTheWireValues()
    {
        ((byte)WearSlot.Shirt).Should().Be(1);
        ((byte)WearSlot.Pants).Should().Be(2);
        ((byte)WearSlot.Hat).Should().Be(3);
    }

    private static HashSet<string> Keys(string file, [CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
        string path = Path.Combine(root, "wasm", "Nosum.Wasm.ViewModels", "Resources", file);
        return XDocument.Load(path).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
    }
}

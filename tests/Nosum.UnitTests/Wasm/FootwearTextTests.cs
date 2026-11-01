// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Xunit;
using static Nosum.UnitTests.Wasm.WasmTestSupport;

namespace Nosum.UnitTests.Wasm;

/// <summary>Every string the footwear feature shows exists in Vietnamese and English (docs/adr/ADR-084, docs/adr/ADR-010).</summary>
public sealed class FootwearTextTests
{
    private static readonly string[] s_files = ["Language.resx", "Language.en.resx"];
    private static readonly string[] s_catalogue = ["Catalogue.resx", "Catalogue.en.resx"];

    [Fact]
    public void Language_FootwearLabelsExistInBothLanguages()
    {
        string[] needed = ["Wardrobe_Slot_Feet", "Tooltip_MoveSpeed"];

        foreach (string file in s_files)
        {
            Keys(file).Should().Contain(needed, file);
        }
    }

    [Fact]
    public void Catalogue_EveryFootwearItemHasANameAndADescriptionInBothLanguages()
    {
        string[] items = ["leather_boots", "traveler_boots", "swift_boots", "winged_boots", "speed_tonic"];
        string[] needed = [.. items.SelectMany(key => new[] { "Item_" + key + "_Name", "Item_" + key + "_Description" })];

        foreach (string file in s_catalogue)
        {
            Keys(file).Should().Contain(needed, file);
        }
    }
}

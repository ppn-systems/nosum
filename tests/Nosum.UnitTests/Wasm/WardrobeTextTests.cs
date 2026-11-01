// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Xml.Linq;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Look;
using Nosum.Gameplay.Looks;
using Xunit;
using static Nosum.UnitTests.Wasm.WasmTestSupport;

namespace Nosum.UnitTests.Wasm;

public sealed class WardrobeTextTests
{
    private static string ResourcesDir([System.Runtime.CompilerServices.CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "wasm", "Nosum.Wasm.ViewModels", "Resources");

    private static readonly string[] s_files = ["Language.resx", "Language.en.resx"];

    [Fact]
    public void Language_EveryHairStyleHasAReadableNameInBothLanguages()
    {
        foreach (string file in s_files)
        {
            for (int id = 1; id <= LookCatalog.HairStyleCount; id++)
            {
                string key = "Wardrobe_HairStyle_Name_" + id;
                XDocument.Load(Path.Combine(ResourcesDir(), file)).Root!.Elements("data")
                    .Single(e => (string)e.Attribute("name")! == key).Element("value")!.Value.Trim()
                    .Should().NotBeNullOrWhiteSpace($"{file} {key}");
            }
        }
    }

    [Fact]
    public void Language_EveryWardrobeKeyTheViewModelReadsHasTextInBothLanguages()
    {
        string[] needed =
        [
            "Wardrobe_Title", "Wardrobe_Skin_Heading", "Wardrobe_HairStyle_Heading", "Wardrobe_HairDye_Heading",
            "Wardrobe_Skin", "Wardrobe_HairDye",
            "Wardrobe_Slot_Shirt", "Wardrobe_Slot_Pants", "Wardrobe_Slot_Hat",
            "Wardrobe_Worn", "Wardrobe_Bag", "Wardrobe_BagEmpty", "Wardrobe_TakeOff", "Wardrobe_HairStyle_Current",
            .. Enumerable.Range(1, LookCatalog.HairStyleCount).Select(id => "Wardrobe_HairStyle_Name_" + id),
            .. Enum.GetValues<LookStatus>().Where(s => s != LookStatus.Ok).Select(s => "Wardrobe_Status_" + s),
            "Wardrobe_Status_Ok",
        ];

        foreach (string file in s_files)
        {
            Keys(file).Should().Contain(needed, file);
        }
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Look;
using Xunit;
using static Nosum.UnitTests.Wasm.WasmTestSupport;

namespace Nosum.UnitTests.Wasm;

public sealed class WardrobeTextTests
{
    private static readonly string[] s_files = ["Language.resx", "Language.en.resx"];

    [Fact]
    public void Language_EveryWardrobeKeyTheViewModelReadsHasTextInBothLanguages()
    {
        string[] needed =
        [
            "Wardrobe_Title", "Wardrobe_Skin_Heading", "Wardrobe_HairStyle_Heading", "Wardrobe_HairDye_Heading",
            "Wardrobe_Skin", "Wardrobe_HairStyle", "Wardrobe_HairDye",
            "Wardrobe_Slot_Shirt", "Wardrobe_Slot_Pants", "Wardrobe_Slot_Hat",
            "Wardrobe_Worn", "Wardrobe_Bag", "Wardrobe_BagEmpty", "Wardrobe_TakeOff",
            .. Enum.GetValues<LookStatus>().Where(s => s != LookStatus.Ok).Select(s => "Wardrobe_Status_" + s),
            "Wardrobe_Status_Ok",
        ];

        foreach (string file in s_files)
        {
            Keys(file).Should().Contain(needed, file);
        }
    }
}

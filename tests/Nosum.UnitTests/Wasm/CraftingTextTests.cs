// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Placeable;
using Nosum.Gameplay.Crafting;
using Nosum.Gameplay.Items;
using Xunit;
using static Nosum.UnitTests.Wasm.WasmTestSupport;

namespace Nosum.UnitTests.Wasm;

public sealed class CraftingTextTests
{
    private static readonly string[] s_files = ["Language.resx", "Language.en.resx"];

    [Fact]
    public void Language_EveryCraftChestAndFurnaceStatusHasATextInBothLanguages()
    {
        string[] needed =
        [
            .. Enum.GetValues<CraftStatus>().Where(s => s != CraftStatus.Ok).Select(s => "Craft_Status_" + s),
            .. Enum.GetValues<ChestStatus>().Where(s => s != ChestStatus.Ok).Select(s => "Chest_Status_" + s),
            .. Enum.GetValues<FurnaceStatus>().Where(s => s != FurnaceStatus.Ok).Select(s => "Furnace_Status_" + s),
        ];

        foreach (string file in s_files)
        {
            Keys(file).Should().Contain(needed, file);
        }
    }

    [Fact]
    public void Language_TheCraftingChestAndFurnacePagesHaveTheirTextInBothLanguages()
    {
        string[] needed =
        [
            "Toolbar_Crafting", "Craft_Heading", "Craft_Materials", "Craft_Makes", "Craft_Button", "Craft_ButtonMany", "Craft_Unlock", "Craft_Locked",
            "Chest_Title", "Chest_YourBag", "Chest_Hint",
            "Furnace_Title", "Furnace_Idle", "Furnace_Working", "Furnace_Ready", "Furnace_Start", "Furnace_Collect",
            .. Enum.GetValues<CraftSkill>().Where(s => s != CraftSkill.None).Select(s => "Craft_Skill_" + s),
        ];

        foreach (string file in s_files)
        {
            Keys(file).Should().Contain(needed, file);
        }
    }

    [Fact]
    public void Language_BothFilesHaveTheSameKeys()
    {
        Keys("Language.resx").Should().BeEquivalentTo(Keys("Language.en.resx"));
    }

    [Fact]
    public void Catalogue_EveryCraftedItemHasANameAndDescriptionInBothLanguages()
    {
        string[] needed = [.. ItemCatalog.All.Where(i => i.Kind is ItemKind.Placeable or ItemKind.Food || i.Id is ItemCatalog.IronOre or ItemCatalog.CopperBar or ItemCatalog.IronBar)
            .SelectMany(i => new[] { $"Item_{i.Key}_Name", $"Item_{i.Key}_Description" })];

        needed.Should().NotBeEmpty();
        Keys("Catalogue.resx").Should().Contain(needed);
        Keys("Catalogue.en.resx").Should().Contain(needed);
    }
}

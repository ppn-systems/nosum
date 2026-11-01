// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Gameplay.Crafting;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Crafting;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.ViewModels.Crafting;
using Xunit;
using static Nosum.UnitTests.Wasm.WasmTestSupport;

namespace Nosum.UnitTests.Wasm;

[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the fake socket owns nothing, pooled packets and channels die with the test.")]
public sealed class CraftingViewModelTests
{
    private static (CraftingViewModel Vm, FakeGameSocket Socket) Create(int[] unlocked, params (int Slot, int ItemId, int Quantity)[] stacks)
    {
        FakeGameSocket socket = new();
        InventoryChannel inventory = LoadedInventory(socket, stacks);
        CraftingChannel crafting = new(socket);
        socket.ReplyWith(_ => new RecipeBook { Unlocked = [.. unlocked] });
        CraftingViewModel vm = new(crafting, inventory, Describer(), Text());
        vm.Open();
        return (vm, socket);
    }

    [Fact]
    public void MaxCraftable_IsWhatTheItemsAllow_AndCraftMaxSendsThatCount()
    {
        // A fence takes two wood: nine wood make four fences.
        (CraftingViewModel vm, FakeGameSocket socket) = Create([RecipeCatalog.WoodFence], (0, ItemCatalog.Wood, 9));
        socket.ReplyWith(_ => new CraftResult { Status = CraftStatus.Ok, Times = 4 });
        vm.Select(RecipeCatalog.WoodFence);

        vm.MaxCraftable.Should().Be(4);
        vm.CraftMax();

        Craft request = (Craft)socket.Requests[^1];
        (request.RecipeId, request.Times).Should().Be(((ushort)RecipeCatalog.WoodFence, (byte)4));
    }

    [Fact]
    public void MaxCraftable_IsCappedAtTheServerBatchLimit()
    {
        // Thirty-six full stacks of wood: far more fences than one request may ask for.
        (int Slot, int ItemId, int Quantity)[] bag = [.. Enumerable.Range(0, 36).Select(i => (i, ItemCatalog.Wood, 99))];
        (CraftingViewModel vm, _) = Create([RecipeCatalog.WoodFence], bag);
        vm.Select(RecipeCatalog.WoodFence);

        vm.MaxCraftable.Should().Be(RecipeCatalog.MaxBatch);
    }

    [Fact]
    public void MaxCraftable_OfALockedRecipe_IsZeroAndCraftMaxSendsNothing()
    {
        (CraftingViewModel vm, FakeGameSocket socket) = Create([], (0, ItemCatalog.Wood, 90));
        vm.Select(RecipeCatalog.WoodFence);
        int before = socket.Requests.Count;

        vm.MaxCraftable.Should().Be(0);
        vm.CanCraftMax.Should().BeFalse();
        vm.CraftMax();

        socket.Requests.Count.Should().Be(before);
    }

    [Fact]
    public void Rows_ListEveryRecipe_WithHaveAndNeedCounts()
    {
        (CraftingViewModel vm, _) = Create([RecipeCatalog.Chest], (0, ItemCatalog.Wood, 30), (1, ItemCatalog.Wood, 5));

        vm.Rows.Should().HaveCount(RecipeCatalog.All.Count);
        RecipeRow chest = vm.Rows.Single(r => r.RecipeId == RecipeCatalog.Chest);
        chest.Inputs.Should().ContainSingle();
        (chest.Inputs[0].Have, chest.Inputs[0].Need, chest.Inputs[0].Enough).Should().Be((35, 50, false));
        chest.Output.ItemId.Should().Be(ItemCatalog.Chest);
    }

    [Fact]
    public void Rows_ARecipeTheServerDidNotUnlock_IsLockedWithItsUnlockText()
    {
        using CultureScope scope = new("en");
        (CraftingViewModel vm, _) = Create([RecipeCatalog.Chest], (0, ItemCatalog.CopperBar, 1), (1, ItemCatalog.IronBar, 1));

        RecipeRow sprinkler = vm.Rows.Single(r => r.RecipeId == RecipeCatalog.Sprinkler);

        sprinkler.Unlocked.Should().BeFalse();
        sprinkler.UnlockText.Should().Be("Requires Farming level 2");
        sprinkler.CanCraft.Should().BeFalse("the items are there but the recipe is locked");
        vm.Rows.Single(r => r.RecipeId == RecipeCatalog.Chest).UnlockText.Should().BeEmpty("a recipe known from the start has no requirement");
    }

    [Fact]
    public void UnlockText_IsLocalized()
    {
        using CultureScope scope = new("vi");
        (CraftingViewModel vm, _) = Create([]);

        vm.Rows.Single(r => r.RecipeId == RecipeCatalog.Furnace).UnlockText.Should().Be("Cần kỹ năng Khai khoáng cấp 1");
    }

    [Fact]
    public void CanCraft_NeedsTheUnlock_TheItemsAndRoomForTheResult()
    {
        (CraftingViewModel vm, _) = Create([RecipeCatalog.WoodFence], (0, ItemCatalog.Wood, 9));
        vm.Select(RecipeCatalog.WoodFence);

        vm.CanCraft(1).Should().BeTrue();
        vm.CanCraft(4).Should().BeTrue();
        vm.CanCraft(5).Should().BeFalse("ten wood are needed and nine are held");
    }

    [Fact]
    public void CanCraft_AFullBagWhereTheInputsKeepTheirSlot_IsRefusedForLackOfRoom()
    {
        // 35 slots of parsnips and one slot of five stone: a path takes one stone, the slot stays, nothing is left for the path.
        (int Slot, int ItemId, int Quantity)[] bag = [.. Enumerable.Range(0, 35).Select(i => (i, ItemCatalog.Parsnip, 99)), (35, ItemCatalog.Stone, 5)];
        (CraftingViewModel vm, _) = Create([RecipeCatalog.StonePath], bag);
        vm.Select(RecipeCatalog.StonePath);

        vm.CanCraft(1).Should().BeFalse();
    }

    [Fact]
    public void CanCraft_AFullBagWhereTheInputsFreeTheirSlot_IsAllowed()
    {
        (int Slot, int ItemId, int Quantity)[] bag = [.. Enumerable.Range(0, 35).Select(i => (i, ItemCatalog.Parsnip, 99)), (35, ItemCatalog.Stone, 1)];
        (CraftingViewModel vm, _) = Create([RecipeCatalog.StonePath], bag);
        vm.Select(RecipeCatalog.StonePath);

        vm.CanCraft(1).Should().BeTrue("the stone leaves its slot, the path takes it");
    }

    [Fact]
    public void Craft_WhenPossible_SendsTheRecipeAndCount()
    {
        (CraftingViewModel vm, FakeGameSocket socket) = Create([RecipeCatalog.WoodFence], (0, ItemCatalog.Wood, 20));
        socket.ReplyWith(_ => new CraftResult { Status = CraftStatus.Ok, Times = 5 });
        vm.Select(RecipeCatalog.WoodFence);

        vm.Craft(5);

        Craft request = (Craft)socket.Requests[^1];
        (request.RecipeId, request.Times).Should().Be(((ushort)RecipeCatalog.WoodFence, (byte)5));
    }

    [Fact]
    public void Craft_WhenTheRulesSayNo_SendsNothing()
    {
        (CraftingViewModel vm, FakeGameSocket socket) = Create([RecipeCatalog.WoodFence], (0, ItemCatalog.Wood, 1));
        vm.Select(RecipeCatalog.WoodFence);
        int before = socket.Requests.Count;

        vm.Craft(1);

        socket.Requests.Count.Should().Be(before);
    }

    [Fact]
    public void Craft_ARefusal_ShowsItsTextInTheStatusLine()
    {
        using CultureScope scope = new("en");
        (CraftingViewModel vm, FakeGameSocket socket) = Create([RecipeCatalog.WoodFence], (0, ItemCatalog.Wood, 20));
        socket.ReplyWith(_ => new CraftResult { Status = CraftStatus.NotEnough });
        vm.Select(RecipeCatalog.WoodFence);

        vm.Craft(1);

        vm.Status.Should().Be("You do not have enough materials.");
    }
}

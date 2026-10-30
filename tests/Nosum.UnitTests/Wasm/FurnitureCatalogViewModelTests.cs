// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.ViewModels.Placement;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

[Collection(SharedUiCulture.Name)]
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the fake socket owns nothing, pooled packets and channels die with the test.")]
public sealed class FurnitureCatalogViewModelTests
{
    private const int AllTab = FurnitureCatalogViewModel.AllTab;
    private const int SeatingTab = (int)FurnitureCategory.Seating + 1;
    private const int TablesTab = (int)FurnitureCategory.Tables + 1;
    private const int PlantsTab = (int)FurnitureCategory.Plants + 1;

    [Fact]
    public void SelectTab_ActiveCategory_ListsOnlyThatCategory()
    {
        using WasmTestSupport.CultureScope scope = new("en");
        FurnitureCatalogViewModel vm = Create(new FakeGameSocket(), Owned(ItemCatalog.OakChair, ItemCatalog.OakTable, ItemCatalog.HousePlant), out _, out _);

        vm.SelectTab(TablesTab);

        vm.ActiveTab.Should().Be(TablesTab);
        vm.Entries.Select(e => e.ItemId).Should().Equal(ItemCatalog.OakTable);
    }

    [Fact]
    public void SetSearch_MatchesTheLocalizedNameIgnoringCase()
    {
        using WasmTestSupport.CultureScope scope = new("en");
        FurnitureCatalogViewModel vm = Create(new FakeGameSocket(), Owned(ItemCatalog.OakChair, ItemCatalog.OakTable, ItemCatalog.BlueCouch), out _, out _);

        vm.SetSearch("OAK");

        vm.Entries.Select(e => e.Name).Should().Equal("Oak chair", "Oak table");
    }

    [Fact]
    public void SetSearch_EmptyText_ListsEverythingOwned()
    {
        using WasmTestSupport.CultureScope scope = new("en");
        FurnitureCatalogViewModel vm = Create(new FakeGameSocket(), Owned(ItemCatalog.OakChair, ItemCatalog.OakTable, ItemCatalog.BlueCouch), out _, out _);
        vm.SetSearch("couch");

        vm.SetSearch(string.Empty);

        vm.Entries.Should().HaveCount(3);
    }

    [Fact]
    public void Entries_AnUnownedPiece_IsNotListed()
    {
        using WasmTestSupport.CultureScope scope = new("en");

        FurnitureCatalogViewModel vm = Create(new FakeGameSocket(), Owned(ItemCatalog.OakChair), out _, out _);

        vm.Entries.Select(e => e.ItemId).Should().Equal(ItemCatalog.OakChair);
        vm.Entries.Single().Count.Should().Be(1);
    }

    [Fact]
    public void Entries_AreSortedByCategoryThenName()
    {
        using WasmTestSupport.CultureScope scope = new("en");

        FurnitureCatalogViewModel vm = Create(new FakeGameSocket(), Owned(ItemCatalog.OakTable, ItemCatalog.OakChair, ItemCatalog.BlueCouch), out _, out _);

        vm.Entries.Select(e => e.ItemId).Should().Equal(ItemCatalog.BlueCouch, ItemCatalog.OakChair, ItemCatalog.OakTable);
    }

    [Fact]
    public void Carry_APieceOnTheHotbar_SelectsItsSlot()
    {
        using WasmTestSupport.CultureScope scope = new("en");
        FakeGameSocket socket = new();
        FurnitureCatalogViewModel vm = Create(socket, [(0, ItemCatalog.Hoe, 1), (3, ItemCatalog.OakChair, 1)], out InventoryChannel inventory, out _);

        vm.Carry(ItemCatalog.OakChair);

        inventory.SelectedSlot.Should().Be(3);
        vm.SelectedItemId.Should().Be(ItemCatalog.OakChair);
        socket.Requests.OfType<MoveSlot>().Should().BeEmpty();
    }

    [Fact]
    public void Carry_APieceInTheBag_MovesItToAnEmptyHotbarSlotAndSelectsIt()
    {
        using WasmTestSupport.CultureScope scope = new("en");
        FakeGameSocket socket = new();
        FurnitureCatalogViewModel vm = Create(socket, [(0, ItemCatalog.Hoe, 1), (20, ItemCatalog.OakChair, 1)], out InventoryChannel inventory, out _);

        vm.Carry(ItemCatalog.OakChair);

        MoveSlot move = socket.Requests.OfType<MoveSlot>().Should().ContainSingle().Subject;
        move.From.Should().Be(20);
        move.To.Should().Be(1);
        inventory.SelectedSlot.Should().Be(1);
    }

    [Fact]
    public void Carry_APieceThePlayerDoesNotOwn_DoesNothing()
    {
        using WasmTestSupport.CultureScope scope = new("en");
        FakeGameSocket socket = new();
        FurnitureCatalogViewModel vm = Create(socket, [(0, ItemCatalog.Hoe, 1)], out InventoryChannel inventory, out _);

        vm.Carry(ItemCatalog.BlueCouch);

        inventory.SelectedSlot.Should().Be(0);
        vm.SelectedItemId.Should().Be(0);
        socket.Requests.OfType<MoveSlot>().Should().BeEmpty();
    }

    [Fact]
    public void PickUp_RequestsPickUpFromThePlacementViewModel()
    {
        using WasmTestSupport.CultureScope scope = new("en");
        FurnitureCatalogViewModel vm = Create(new FakeGameSocket(), Owned(ItemCatalog.OakChair), out _, out PlacementViewModel placement);

        vm.PickUp();

        placement.TakeRequests().Should().Be(PlacementRequests.PickUp);
    }

    [Fact]
    public void SelectTab_EmptyCategory_ShowsTheEmptyMessageOfTheSearch()
    {
        using WasmTestSupport.CultureScope scope = new("en");
        FurnitureCatalogViewModel vm = Create(new FakeGameSocket(), Owned(ItemCatalog.OakChair), out _, out _);

        vm.SelectTab(PlantsTab);

        vm.Entries.Should().BeEmpty();
        vm.EmptyText.Should().Be("No furniture matches.");
    }

    [Fact]
    public void SelectTab_ToTheTabAlreadyShown_DoesNotRaiseChanged()
    {
        using WasmTestSupport.CultureScope scope = new("en");
        FurnitureCatalogViewModel vm = Create(new FakeGameSocket(), Owned(ItemCatalog.OakChair), out _, out _);
        vm.SelectTab(SeatingTab);
        int changed = 0;
        vm.Changed += () => changed++;

        vm.SelectTab(SeatingTab);

        changed.Should().Be(0);
    }

    [Fact]
    public void Tabs_StartWithAllAndListEveryCategoryWithItsLabel()
    {
        using WasmTestSupport.CultureScope scope = new("en");
        FurnitureCatalogViewModel vm = Create(new FakeGameSocket(), Owned(ItemCatalog.OakChair), out _, out _);

        vm.Tabs[0].Should().Be((AllTab, "All"));
        vm.Tabs.Should().HaveCount(9);
        vm.Tabs[SeatingTab].Label.Should().Be("Seating");
    }

    private static FurnitureCatalogViewModel Create(FakeGameSocket socket, (int Slot, int ItemId, int Quantity)[] stacks, out InventoryChannel inventory, out PlacementViewModel placement)
    {
        inventory = WasmTestSupport.LoadedInventory(socket, stacks);
        placement = new PlacementViewModel(WasmTestSupport.Localizer<Catalogue>(), WasmTestSupport.Text());
        return new FurnitureCatalogViewModel(
            Furniture(),
            inventory,
            placement,
            WasmTestSupport.Describer(),
            WasmTestSupport.Localizer<Catalogue>(),
            WasmTestSupport.Text());
    }

    private static (int Slot, int ItemId, int Quantity)[] Owned(params int[] items)
        => items.Select((item, slot) => (slot, item, 1)).ToArray();

    private static IReadOnlyList<PlaceableDefinition> Furniture()
    {
        return
        [
            Piece(ItemCatalog.OakChair, FurnitureCategory.Seating),
            Piece(ItemCatalog.BlueCouch, FurnitureCategory.Seating),
            Piece(ItemCatalog.OakTable, FurnitureCategory.Tables),
            Piece(ItemCatalog.HousePlant, FurnitureCategory.Plants),
        ];
    }

    private static PlaceableDefinition Piece(int itemId, FurnitureCategory category)
        => new(itemId, "furniture", new PlaceableView(0, 0, 16, 16, 1, 1)) { Category = category };
}

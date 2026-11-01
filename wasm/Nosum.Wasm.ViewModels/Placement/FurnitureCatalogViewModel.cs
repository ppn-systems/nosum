// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Localization;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Placement;

/// <summary>One owned piece of furniture in the catalogue panel.</summary>
/// <param name="ItemId">The inventory item the piece is made from.</param>
/// <param name="Name">The localized name.</param>
/// <param name="Count">How many the player carries.</param>
/// <param name="Category">The group the piece is sorted into.</param>
/// <param name="BlocksWalking">Whether the piece blocks walking.</param>
public sealed record FurnitureEntry(int ItemId, string Name, int Count, FurnitureCategory Category, bool BlocksWalking);

/// <summary>
/// View model of the furniture catalogue panel: the owned furniture, grouped by category and filtered by a search text. Choosing a piece
/// carries it into the held hotbar slot, so the placement ghost (<c>PlacementActions</c>) previews it; the panel's Place and Pick up buttons
/// use the same placement flow as the keys. The list is rebuilt when the inventory, the search, the category or the language changes, never per frame.
/// </summary>
public sealed class FurnitureCatalogViewModel : IObservableViewModel, IDisposable
{
    /// <summary>The tab id that lists every category.</summary>
    public const int AllTab = 0;

    private readonly Piece[] _pieces;
    private readonly InventoryChannel _inventory;
    private readonly PlacementViewModel _placement;
    private readonly ItemDescriber _describer;
    private readonly IStringLocalizer<Catalogue> _catalogue;
    private readonly IStringLocalizer<Language> _text;
    private List<FurnitureEntry> _owned = [];
    private IReadOnlyList<FurnitureEntry> _entries = [];
    private IReadOnlyList<SlotDisplay> _slots = [];
    private IReadOnlyList<(int Id, string Label)> _tabs = [];
    private FurnitureCategory? _category;
    private string _search = string.Empty;
    private int _selectedItemId;

    /// <summary>Initializes the view model and starts following the inventory, the placement panel and the language.</summary>
    /// <param name="placeables">The placeable catalogue; its furniture is listed (the game passes <c>PlaceableCatalog.All</c>).</param>
    /// <param name="inventory">The inventory mirror: what the player owns and which hotbar slot is held.</param>
    /// <param name="placement">The arrange panel: its visibility and the pick-up request.</param>
    /// <param name="describer">Turns owned pieces into slot displays for the grid.</param>
    /// <param name="catalogue">Item names.</param>
    /// <param name="text">UI text.</param>
    public FurnitureCatalogViewModel(
        IReadOnlyList<PlaceableDefinition> placeables,
        InventoryChannel inventory,
        PlacementViewModel placement,
        ItemDescriber describer,
        IStringLocalizer<Catalogue> catalogue,
        IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(placeables);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(describer);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(text);

        _pieces = Pieces(placeables);
        _inventory = inventory;
        _placement = placement;
        _describer = describer;
        _catalogue = catalogue;
        _text = text;

        inventory.Changed += this.Rebuild;
        placement.Changed += this.OnPlacementChanged;
        UiCulture.Changed += this.Rebuild;
        this.Rebuild();
    }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>Gets whether the panel is shown: the player is in a place where furniture can be placed.</summary>
    public bool IsVisible => _placement.IsVisible;

    /// <summary>Gets the localized panel title.</summary>
    public string Title => _text["Furniture_Title"];

    /// <summary>Gets the localized placeholder (and accessible name) of the search field.</summary>
    public string SearchPlaceholder => _text["Furniture_Search"];

    /// <summary>Gets the localized label of the button that carries the selected piece for placement.</summary>
    public string PlaceLabel => _text["Furniture_Place"];

    /// <summary>Gets the localized label of the button that takes the piece in front back to the inventory.</summary>
    public string PickUpLabel => _text["Furniture_PickUp"];

    /// <summary>Gets the category tabs: the tab id and its localized label. The first tab is <see cref="AllTab"/>.</summary>
    public IReadOnlyList<(int Id, string Label)> Tabs => _tabs;

    /// <summary>Gets the id of the active category tab.</summary>
    public int ActiveTab => _category is { } category ? CategoryTab(category) : AllTab;

    /// <summary>Gets the search text as typed.</summary>
    public string Search => _search;

    /// <summary>Gets the owned furniture the panel lists: the active category, matching the search, sorted by category then name.</summary>
    public IReadOnlyList<FurnitureEntry> Entries => _entries;

    /// <summary>Gets the grid slots of <see cref="Entries"/>, in the same order; the selected piece is marked.</summary>
    public IReadOnlyList<SlotDisplay> Slots => _slots;

    /// <summary>Gets the item id of the selected piece, or zero.</summary>
    public int SelectedItemId => _selectedItemId;

    /// <summary>Gets the selected piece when it is listed, otherwise <see langword="null"/>.</summary>
    public FurnitureEntry? Selected
    {
        get
        {
            foreach (FurnitureEntry entry in _entries)
            {
                if (entry.ItemId == _selectedItemId)
                {
                    return entry;
                }
            }

            return null;
        }
    }

    /// <summary>Gets the localized line describing the selected piece (name, count, whether it blocks walking), or <see langword="null"/> when none is selected.</summary>
    public string? DetailText
    {
        get
        {
            FurnitureEntry? selected = this.Selected;
            if (selected is null)
            {
                return null;
            }

            string owned = string.Format(CultureInfo.CurrentCulture, _text["Furniture_Owned"].Value, selected.Count);
            string walking = _text[selected.BlocksWalking ? "Furniture_BlocksWalking" : "Furniture_WalkOver"].Value;
            return string.Format(CultureInfo.CurrentCulture, _text["Furniture_Detail"].Value, selected.Name, owned, walking);
        }
    }

    /// <summary>Gets the localized message shown instead of the list: none owned at all, or nothing matches the search. Null when the list has entries.</summary>
    public string? EmptyText
    {
        get
        {
            if (_owned.Count == 0)
            {
                return _text["Furniture_Empty"].Value;
            }

            return _entries.Count == 0 ? _text["Furniture_NoMatch"].Value : null;
        }
    }

    /// <summary>Sets the search text; the list matches the localized names, ignoring case, and an empty search lists everything.</summary>
    /// <param name="text">What was typed.</param>
    public void SetSearch(string text)
    {
        _search = text ?? string.Empty;
        this.Filter();
        this.Changed?.Invoke();
    }

    /// <summary>Shows one category tab; <see cref="AllTab"/> lists every category.</summary>
    /// <param name="tab">The tab id.</param>
    public void SelectTab(int tab)
    {
        FurnitureCategory? next;
        if (tab == AllTab)
        {
            next = null;
        }
        else if (tab - 1 is >= 0 and <= (int)FurnitureCategory.Equipment)
        {
            next = (FurnitureCategory)(tab - 1);
        }
        else
        {
            return;
        }

        if (next == _category)
        {
            return;
        }

        _category = next;
        this.Filter();
        this.Changed?.Invoke();
    }

    /// <summary>Selects the piece at a grid position (a click on the grid).</summary>
    /// <param name="index">The index in <see cref="Entries"/>.</param>
    public void Select(int index)
    {
        if ((uint)index >= (uint)_entries.Count)
        {
            return;
        }

        _selectedItemId = _entries[index].ItemId;
        this.Filter();
        this.Changed?.Invoke();
    }

    /// <summary>
    /// Chooses a piece to carry for placement: it becomes the held item, so the placement ghost previews it. A piece already on the hotbar is
    /// selected; a piece in the bag is moved into an empty hotbar slot first (or into the held slot when the hotbar is full). Nothing happens
    /// for a piece the player does not own.
    /// </summary>
    /// <param name="itemId">The item to carry.</param>
    public void Carry(int itemId)
    {
        if (itemId <= 0 || _inventory.CountOf(itemId) <= 0)
        {
            return;
        }

        _selectedItemId = itemId;
        _placement.ClearDismissed();
        int hotbar = Math.Min(InventoryLimits.HotbarSlots, _inventory.Capacity);
        int held = FindSlot(0, hotbar, itemId);
        if (held < 0)
        {
            int from = FindSlot(hotbar, _inventory.Capacity, itemId);
            if (from < 0)
            {
                return;
            }

            held = FindSlot(0, hotbar, 0);
            if (held < 0)
            {
                held = _inventory.SelectedSlot;
            }

            _inventory.Move(from, held);
        }

        _inventory.Select(held);
        this.Filter();
        this.Changed?.Invoke();
    }

    /// <summary>Asks the game to take the piece in front of the farmer back into the inventory.</summary>
    public void PickUp() => _placement.Request(PlacementRequests.PickUp);

    /// <summary>Closes arrange mode from the panel's close button or Escape: a carried piece goes back where it was.</summary>
    public void Exit() => _placement.Dismiss();

    /// <summary>The localized text of the close button.</summary>
    public string CloseLabel => _text["Panel_Close"];

    /// <inheritdoc/>
    public void Dispose()
    {
        _inventory.Changed -= this.Rebuild;
        _placement.Changed -= this.OnPlacementChanged;
        UiCulture.Changed -= this.Rebuild;
    }

    private static Piece[] Pieces(IReadOnlyList<PlaceableDefinition> placeables)
    {
        List<Piece> pieces = new(placeables.Count);
        foreach (PlaceableDefinition definition in placeables)
        {
            // The furniture, and the crafted pieces the arrange panel also places (a chest, a furnace, a sprinkler): the panel opens for them, so it lists them.
            if (definition.Category is { } category
                && ItemCatalog.TryGet(definition.ItemId, out ItemDefinition? item)
                && (item.Kind == ItemKind.Furniture || definition.Kind is PlaceableKind.Chest or PlaceableKind.Furnace or PlaceableKind.Sprinkler))
            {
                pieces.Add(new Piece(definition.ItemId, item.Key, category, definition.BlocksWalking));
            }
        }

        return pieces.ToArray();
    }

    private static int CategoryTab(FurnitureCategory category) => (int)category + 1;

    private static int CompareEntries(FurnitureEntry first, FurnitureEntry second)
    {
        int byCategory = first.Category.CompareTo(second.Category);
        if (byCategory != 0)
        {
            return byCategory;
        }

        int byName = string.Compare(first.Name, second.Name, StringComparison.CurrentCulture);
        return byName != 0 ? byName : first.ItemId.CompareTo(second.ItemId);
    }

    private static string CategoryKey(FurnitureCategory category) => category switch
    {
        FurnitureCategory.Seating => "Furniture_Category_Seating",
        FurnitureCategory.Tables => "Furniture_Category_Tables",
        FurnitureCategory.Beds => "Furniture_Category_Beds",
        FurnitureCategory.Storage => "Furniture_Category_Storage",
        FurnitureCategory.Lighting => "Furniture_Category_Lighting",
        FurnitureCategory.Plants => "Furniture_Category_Plants",
        FurnitureCategory.Decor => "Furniture_Category_Decor",
        FurnitureCategory.Equipment => "Furniture_Category_Equipment",
        _ => "Furniture_Category_Rugs",
    };

    private void Rebuild()
    {
        List<(int Id, string Label)> tabs = [(AllTab, _text["Furniture_Category_All"].Value)];
        foreach (FurnitureCategory category in Enum.GetValues<FurnitureCategory>())
        {
            tabs.Add((CategoryTab(category), _text[CategoryKey(category)].Value));
        }

        _tabs = tabs;

        List<FurnitureEntry> owned = new(_pieces.Length);
        foreach (Piece piece in _pieces)
        {
            int count = _inventory.CountOf(piece.ItemId);
            if (count > 0)
            {
                owned.Add(new FurnitureEntry(piece.ItemId, _catalogue["Item_" + piece.Key + "_Name"].Value, count, piece.Category, piece.BlocksWalking));
            }
        }

        owned.Sort(CompareEntries);
        _owned = owned;
        this.Filter();
        this.Changed?.Invoke();
    }

    private void Filter()
    {
        string needle = _search.Trim();
        List<FurnitureEntry> entries = new(_owned.Count);
        foreach (FurnitureEntry entry in _owned)
        {
            if ((_category is null || entry.Category == _category)
                && (needle.Length == 0 || entry.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase)))
            {
                entries.Add(entry);
            }
        }

        List<SlotDisplay> slots = new(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            FurnitureEntry entry = entries[i];
            slots.Add(_describer.Describe(i, entry.ItemId, entry.Count, entry.ItemId == _selectedItemId));
        }

        _entries = entries;
        _slots = slots;
    }

    private int FindSlot(int from, int to, int itemId)
    {
        for (int i = from; i < to; i++)
        {
            if (_inventory.SlotAt(i).ItemId == itemId)
            {
                return i;
            }
        }

        return -1;
    }

    private void OnPlacementChanged() => this.Changed?.Invoke();

    private readonly record struct Piece(int ItemId, string Key, FurnitureCategory Category, bool BlocksWalking);
}

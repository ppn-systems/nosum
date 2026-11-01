// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Placeable;
using Nosum.Gameplay.Crafting;
using Nosum.Gameplay.Inventory;
using Nosum.Wasm.Socket.Crafting;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Crafting;

/// <summary>One thing a recipe consumes, with what the player has.</summary>
/// <param name="Item">The item as a slot (quantity is what one craft needs).</param>
/// <param name="Have">How many the player holds.</param>
/// <param name="Need">How many one craft consumes.</param>
public sealed record InputDisplay(SlotDisplay Item, int Have, int Need)
{
    /// <summary>Gets whether the player has enough for one craft.</summary>
    public bool Enough => this.Have >= this.Need;
}

/// <summary>One recipe row of the crafting page.</summary>
/// <param name="RecipeId">The recipe's id.</param>
/// <param name="Output">The item made, as a slot (quantity is what one craft makes).</param>
/// <param name="Unlocked">Whether the server says the recipe is unlocked.</param>
/// <param name="UnlockText">The localized unlock requirement ("Requires Farming level 2"); empty for a recipe known from the start.</param>
/// <param name="Inputs">What one craft consumes.</param>
/// <param name="CanCraft">Whether one craft is possible now: unlocked, enough items and room for the result.</param>
public sealed record RecipeRow(int RecipeId, SlotDisplay Output, bool Unlocked, string UnlockText, IReadOnlyList<InputDisplay> Inputs, bool CanCraft);

/// <summary>
/// View model of the crafting page (docs/adr/ADR-024 D7): every recipe as a grid cell, locked ones greyed with their unlock text, and for the selected recipe
/// the inputs with have/need counts and a craft button. The buttons are enabled by the same rules the server applies (<see cref="RecipeRules"/>), but the server decides.
/// </summary>
public sealed class CraftingViewModel : PanelViewModel, IDisposable
{
    private readonly CraftingChannel _crafting;
    private readonly InventoryChannel _inventory;
    private readonly ItemDescriber _describer;
    private int _selected = RecipeCatalog.All[0].Id;

    /// <summary>Initializes the view model.</summary>
    /// <param name="crafting">The crafting channel.</param>
    /// <param name="inventory">The inventory mirror: what the player has.</param>
    /// <param name="describer">Describes items.</param>
    /// <param name="text">UI text.</param>
    public CraftingViewModel(CraftingChannel crafting, InventoryChannel inventory, ItemDescriber describer, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(crafting);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(describer);

        _crafting = crafting;
        _inventory = inventory;
        _describer = describer;
        crafting.Changed += this.Raise;
        crafting.Completed += this.OnCompleted;
        inventory.Changed += this.Raise;
    }

    /// <summary>Gets the id of the selected recipe.</summary>
    public int SelectedId => _selected;

    /// <summary>Gets every recipe, in catalogue order.</summary>
    public IReadOnlyList<RecipeRow> Rows
    {
        get
        {
            InventoryGrid grid = this.GridOfInventory();
            List<RecipeRow> rows = new(RecipeCatalog.All.Count);
            foreach (RecipeDefinition recipe in RecipeCatalog.All)
            {
                rows.Add(this.RowOf(recipe, grid));
            }

            return rows;
        }
    }

    /// <summary>Gets the selected recipe's row.</summary>
    public RecipeRow Selected
    {
        get
        {
            _ = RecipeCatalog.TryGet(_selected, out RecipeDefinition? recipe);
            return this.RowOf(recipe ?? RecipeCatalog.All[0], this.GridOfInventory());
        }
    }

    /// <summary>Gets the output slots of every recipe as a grid of slots; the slot index is the recipe id.</summary>
    public IReadOnlyList<SlotDisplay> Cells
    {
        get
        {
            List<SlotDisplay> cells = [];
            foreach (RecipeRow row in this.Rows)
            {
                cells.Add(row.Output with { Index = row.RecipeId, Quantity = 0, IsSelected = row.RecipeId == _selected });
            }

            return cells;
        }
    }

    /// <summary>Gets the localized "makes" line of the selected recipe.</summary>
    public string MakesText
    {
        get
        {
            RecipeRow row = this.Selected;
            return this.Text("Craft_Makes", row.Output.Name, row.Output.Quantity);
        }
    }

    /// <summary>Gets the localized text of the craft button.</summary>
    public string CraftLabel => this.Text("Craft_Button");

    /// <summary>Gets the localized text of the batch craft button.</summary>
    public string CraftManyLabel => this.Text("Craft_ButtonMany", BatchSize);

    /// <summary>Gets how many crafts the batch button asks for.</summary>
    public static int BatchSize => 5;

    /// <summary>Gets whether the selected recipe can be crafted once right now.</summary>
    public bool CanCraftOne => this.CanCraft(1);

    /// <summary>Gets whether the selected recipe can be crafted <see cref="BatchSize"/> times right now.</summary>
    public bool CanCraftBatch => this.CanCraft(BatchSize);

    /// <summary>Crafts the selected recipe once.</summary>
    public void CraftOne() => this.Craft(1);

    /// <summary>Crafts the selected recipe <see cref="BatchSize"/> times.</summary>
    public void CraftBatch() => this.Craft(BatchSize);

    /// <summary>
    /// Gets how many times the selected recipe can be crafted right now: as many as the items allow (capped at the server's batch limit)
    /// and the bag has room for, or zero when it cannot be made at all. The server checks the same count again when the request arrives.
    /// </summary>
    public int MaxCraftable
    {
        get
        {
            if (!RecipeCatalog.TryGet(_selected, out RecipeDefinition? recipe) || !_crafting.IsUnlocked(recipe.Id))
            {
                return 0;
            }

            InventoryGrid grid = this.GridOfInventory();
            int upper = RecipeCatalog.MaxBatch;
            foreach (RecipeInput input in recipe.Inputs)
            {
                upper = Math.Min(upper, grid.CountOf(input.ItemId) / input.Quantity);
            }

            // The items bound the count; the room for the result can still lower it, so step down until the rules agree.
            for (int times = upper; times >= 1; times--)
            {
                if (RecipeRules.CanCraft(recipe, times, grid) == InventoryVerdict.Ok)
                {
                    return times;
                }
            }

            return 0;
        }
    }

    /// <summary>Gets whether the selected recipe can be crafted at least once right now.</summary>
    public bool CanCraftMax => this.MaxCraftable > 0;

    /// <summary>Gets the localized text of the button that crafts as many times as possible, with the count it would make.</summary>
    public string CraftMaxLabel => this.Text("Craft_ButtonMax", this.MaxCraftable);

    /// <summary>Crafts the selected recipe as many times as the items and the bag allow; does nothing when that is none.</summary>
    public void CraftMax()
    {
        int times = this.MaxCraftable;
        if (times > 0)
        {
            this.Craft(times);
        }
    }

    /// <summary>Called when the page opens: reads the recipe book.</summary>
    public void Open()
    {
        this.ClearStatus();
        _crafting.Load();
    }

    /// <summary>Selects a recipe.</summary>
    /// <param name="recipeId">The recipe's id.</param>
    public void Select(int recipeId)
    {
        if (RecipeCatalog.TryGet(recipeId, out _) && recipeId != _selected)
        {
            _selected = recipeId;
            this.Raise();
        }
    }

    /// <summary>Asks the server to craft the selected recipe; does nothing when the rules say it cannot work.</summary>
    /// <param name="times">How many crafts.</param>
    public void Craft(int times)
    {
        if (times is < 1 or > RecipeCatalog.MaxBatch || !RecipeCatalog.TryGet(_selected, out RecipeDefinition? recipe))
        {
            return;
        }

        if (!this.CanCraftTimes(recipe, times))
        {
            return;
        }

        _crafting.Craft(recipe.Id, times);
    }

    /// <summary>Tells whether the selected recipe can be crafted a number of times now.</summary>
    /// <param name="times">How many crafts.</param>
    /// <returns><see langword="true"/> when unlocked, enough items and room.</returns>
    public bool CanCraft(int times) => RecipeCatalog.TryGet(_selected, out RecipeDefinition? recipe) && this.CanCraftTimes(recipe, times);

    /// <inheritdoc/>
    public void Dispose()
    {
        _crafting.Changed -= this.Raise;
        _crafting.Completed -= this.OnCompleted;
        _inventory.Changed -= this.Raise;
    }

    private bool CanCraftTimes(RecipeDefinition recipe, int times)
        => _crafting.IsUnlocked(recipe.Id) && RecipeRules.CanCraft(recipe, times, this.GridOfInventory()) == InventoryVerdict.Ok;

    private void OnCompleted(CraftStatus status)
    {
        if (status == CraftStatus.Ok)
        {
            this.ClearStatus();
        }
        else
        {
            this.SetStatus(this.Text("Craft_Status_" + status));
        }
    }

    private RecipeRow RowOf(RecipeDefinition recipe, InventoryGrid grid)
    {
        bool unlocked = _crafting.IsUnlocked(recipe.Id);
        List<InputDisplay> inputs = new(recipe.Inputs.Count);
        foreach (RecipeInput input in recipe.Inputs)
        {
            inputs.Add(new InputDisplay(_describer.Describe(input.ItemId, input.ItemId, input.Quantity), grid.CountOf(input.ItemId), input.Quantity));
        }

        string unlock = recipe.Unlock.IsKnown ? string.Empty : this.Text("Craft_Unlock", this.Text("Craft_Skill_" + recipe.Unlock.Skill), recipe.Unlock.Level);
        SlotDisplay output = _describer.Describe(recipe.Id, recipe.OutputItemId, recipe.OutputQuantity);
        bool can = unlocked && RecipeRules.CanCraft(recipe, 1, grid) == InventoryVerdict.Ok;
        return new RecipeRow(recipe.Id, output, unlocked, unlock, inputs, can);
    }

    // The player's inventory as a pure grid, for the same room and count rules the server uses.
    private InventoryGrid GridOfInventory()
    {
        int capacity = _inventory.Capacity;
        InventoryGrid grid = new(Math.Max(1, capacity));
        for (int i = 0; i < capacity; i++)
        {
            SlotInfo slot = _inventory.SlotAt(i);
            if (slot.ItemId != 0 && slot.Quantity > 0)
            {
                grid.Load(i, new ItemSlot(slot.ItemId, slot.Quantity));
            }
        }

        return grid;
    }
}

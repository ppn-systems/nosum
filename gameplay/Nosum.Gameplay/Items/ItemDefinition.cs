// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Items;

/// <summary>
/// One kind of item: a stable numeric id (wire and database), a stable key (localization catalogue
/// entries <c>item.&lt;key&gt;.name</c> and <c>.description</c>), how it stacks and what it does.
/// The id and key never change once released (docs/adr/ADR-011); display text is not stored here.
/// </summary>
public sealed class ItemDefinition
{
    /// <summary>Initializes a definition.</summary>
    /// <param name="id">Stable numeric id, greater than zero.</param>
    /// <param name="key">Stable key, for example <c>hoe</c>.</param>
    /// <param name="kind">What the item is for.</param>
    /// <param name="maxStack">Largest stack in one inventory slot.</param>
    /// <param name="tool">The tool, for <see cref="ItemKind.Tool"/> items.</param>
    /// <param name="cropId">The crop a seed grows, for <see cref="ItemKind.Seed"/> items.</param>
    /// <param name="wear">Where the item is worn, for <see cref="ItemKind.Clothing"/> items.</param>
    /// <param name="treeVariant">The tree species a tree seed grows, for <see cref="ItemKind.TreeSeed"/> items.</param>
    /// <param name="foodEnergy">The energy eating one restores, for edible items (<see cref="ItemKind.Food"/> and raw produce); zero for items that cannot be eaten.</param>
    public ItemDefinition(int id, string key, ItemKind kind, int maxStack, ToolKind tool = ToolKind.None, int cropId = 0, WearSlot wear = WearSlot.None, byte treeVariant = 0, int foodEnergy = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStack);
        ArgumentOutOfRangeException.ThrowIfNegative(foodEnergy);

        this.Id = id;
        this.Key = key;
        this.Kind = kind;
        this.MaxStack = maxStack;
        this.Tool = tool;
        this.CropId = cropId;
        this.Wear = wear;
        this.TreeVariant = treeVariant;
        this.FoodEnergy = foodEnergy;
    }

    /// <summary>Gets the stable numeric id.</summary>
    public int Id { get; }

    /// <summary>Gets the stable key.</summary>
    public string Key { get; }

    /// <summary>Gets what the item is for.</summary>
    public ItemKind Kind { get; }

    /// <summary>Gets the largest stack in one slot.</summary>
    public int MaxStack { get; }

    /// <summary>Gets the tool kind; <see cref="ToolKind.None"/> for items that are not tools.</summary>
    public ToolKind Tool { get; }

    /// <summary>Gets the crop a seed grows; zero for other items.</summary>
    public int CropId { get; }

    /// <summary>Gets where the item is worn; <see cref="WearSlot.None"/> for items that are not clothing.</summary>
    public WearSlot Wear { get; }

    /// <summary>Gets what the shipping bin pays for one item, in coins; zero when the bin does not buy it (docs/adr/ADR-025). Balance data.</summary>
    public int SellPrice { get; init; }

    /// <summary>Gets the cell of the placeholder objects sheet (<c>assets/Maps/springobjects.png</c>, 24 columns of 16 px cells) that pictures the item; zero when another sheet does (docs/adr/ADR-025 D6).</summary>
    public int ObjectIcon { get; init; }

    /// <summary>Gets the tree species a tree seed grows (<c>Nosum.Gameplay.Nodes.TreeVariant</c>); zero for other items.</summary>
    public byte TreeVariant { get; }

    /// <summary>Gets the energy eating one of the item restores (docs/adr/ADR-023 D2); zero for items that cannot be eaten.</summary>
    public int FoodEnergy { get; }

    /// <summary>Gets whether the item can be eaten.</summary>
    public bool IsEdible => this.FoodEnergy > 0;

    /// <summary>
    /// Gets whether the item may be sold, traded or put in a co-op warehouse. Tools are bound to their owner so nobody can
    /// lose the means to farm.
    /// </summary>
    public bool IsTradable => this.Kind != ItemKind.Tool;
}

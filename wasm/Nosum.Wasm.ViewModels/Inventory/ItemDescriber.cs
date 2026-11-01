// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Items;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Inventory;

/// <summary>Turns item ids and quantities into localized <see cref="SlotDisplay"/>s for any panel that shows items (market, trade, warehouse, wardrobe).</summary>
public sealed class ItemDescriber
{
    private readonly IItemIconSource _icons;
    private readonly IStringLocalizer<Catalogue> _catalogue;
    private readonly IStringLocalizer<Language> _text;

    /// <summary>Initializes the describer.</summary>
    /// <param name="icons">Where item pictures are.</param>
    /// <param name="catalogue">Item names and descriptions.</param>
    /// <param name="text">UI text.</param>
    public ItemDescriber(IItemIconSource icons, IStringLocalizer<Catalogue> catalogue, IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(text);

        _icons = icons;
        _catalogue = catalogue;
        _text = text;
    }

    /// <summary>Describes a stack for a slot control.</summary>
    /// <param name="index">The slot or row index the control reports on click.</param>
    /// <param name="itemId">Item id; zero for an empty slot.</param>
    /// <param name="quantity">How many.</param>
    /// <param name="selected">Whether the slot is marked.</param>
    /// <returns>The display.</returns>
    public SlotDisplay Describe(int index, int itemId, int quantity, bool selected = false)
    {
        if (itemId == 0)
        {
            return new SlotDisplay(index, 0, 0, string.Empty, string.Empty, null, selected);
        }

        ItemIcon? icon = _icons.TryGet(itemId, out ItemIcon found) ? found : null;
        if (!ItemCatalog.TryGet(itemId, out ItemDefinition? item))
        {
            return new SlotDisplay(index, itemId, quantity, string.Empty, string.Empty, icon, selected);
        }

        string name = _catalogue["Item_" + item.Key + "_Name"];
        string description = _catalogue["Item_" + item.Key + "_Description"];
        return new SlotDisplay(index, itemId, quantity, name, ItemTooltip.Build(item, name, description, quantity, _text), icon, selected);
    }

    /// <summary>Returns an item's localized name.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>The name; empty for an id this client does not know.</returns>
    public string NameOf(int itemId) => ItemCatalog.TryGet(itemId, out ItemDefinition? item) ? _catalogue["Item_" + item.Key + "_Name"].Value : string.Empty;

    /// <summary>Returns an item's localized description, the one line that says what it is.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>The description; empty for an id this client does not know.</returns>
    public string DescriptionOf(int itemId) => ItemCatalog.TryGet(itemId, out ItemDefinition? item) ? _catalogue["Item_" + item.Key + "_Description"].Value : string.Empty;

    /// <summary>Returns an item's effect lines (food, walking speed, running), one per line of the tooltip.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>The effect lines; empty when the item has no effect or the id is unknown.</returns>
    public List<string> EffectsOf(int itemId) => ItemCatalog.TryGet(itemId, out ItemDefinition? item) ? ItemTooltip.Effects(item, _text) : [];
}

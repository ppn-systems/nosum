// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Energy;
using Nosum.Gameplay.Items;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Inventory;

/// <summary>
/// Builds an item's tooltip: name, description, one line per stat the item has (tool tier, weapon damage, node damage, energy, water capacity,
/// sell price) and the stack quantity. Every line is localized here, so the panels that show items share one layout.
/// </summary>
public static class ItemTooltip
{
    /// <summary>Builds the tooltip of a known item.</summary>
    /// <param name="item">The item definition.</param>
    /// <param name="name">Its localized name.</param>
    /// <param name="description">Its localized description.</param>
    /// <param name="quantity">How many are stacked; shown only for stackable items.</param>
    /// <param name="text">UI text.</param>
    /// <returns>The tooltip, one line per row.</returns>
    public static string Build(ItemDefinition item, string name, string description, int quantity, IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(text);

        List<string> lines = [name, description];

        if (item.Kind == ItemKind.Tool)
        {
            lines.Add(string.Format(CultureInfo.CurrentCulture, text["Tooltip_Tier"].Value, text[TierKey(item.Tier)].Value));
        }

        int weaponDamage = CombatRules.WeaponDamage(item.Id);
        if (weaponDamage > 0)
        {
            lines.Add(Line(text, "Tooltip_Damage", weaponDamage));
        }

        if (item.Tool is ToolKind.Axe or ToolKind.Pickaxe)
        {
            lines.Add(Line(text, "Tooltip_NodeDamage", ToolRules.DamagePerHit(item.Tier)));
        }

        if (item.Tool == ToolKind.WateringCan)
        {
            lines.Add(Line(text, "Tooltip_WaterCapacity", ToolRules.WateringCanCapacity(item.Tier)));
        }

        if (item.Tool is ToolKind.Hoe or ToolKind.WateringCan)
        {
            int area = ToolRules.AreaLength(item.Tier);
            lines.Add(Line(text, area == 1 ? "Tooltip_Area_One" : "Tooltip_Area", area));
        }

        if (EnergyRules.BaseCost(item.Tool) > 0)
        {
            lines.Add(Line(text, "Tooltip_ToolEnergy", EnergyRules.Cost(item.Tool, 0, item.Tier)));
        }

        lines.AddRange(Effects(item, text));

        if (item.SellPrice > 0)
        {
            lines.Add(Line(text, "Tooltip_Sell", item.SellPrice));
        }

        if (item.MaxStack > 1)
        {
            lines.Add(Line(text, "Inventory_Quantity", quantity));
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// The effect lines of an item: what it does to the player, one line each, in the order the tooltip shows them (food, walking speed,
    /// running). The shop's detail strip shows the same lines, so a price is read next to what the item does.
    /// </summary>
    /// <param name="item">The item definition.</param>
    /// <param name="text">UI text.</param>
    /// <returns>The effect lines; empty for an item with no effect.</returns>
    public static List<string> Effects(ItemDefinition item, IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(text);

        List<string> lines = [];
        if (item.IsEdible)
        {
            lines.Add(Line(text, "Tooltip_Energy", item.FoodEnergy));
        }

        if (item.MoveSpeedPercent > 0)
        {
            lines.Add(Line(text, "Tooltip_MoveSpeed", item.MoveSpeedPercent));
        }

        if (item.RunEnergySavePercent > 0)
        {
            lines.Add(Line(text, "Tooltip_RunEnergySave", item.RunEnergySavePercent));
        }

        return lines;
    }

    private static string Line(IStringLocalizer<Language> text, string key, int value)
        => string.Format(CultureInfo.CurrentCulture, text[key].Value, value);

    private static string TierKey(ToolTier tier) => tier switch
    {
        ToolTier.Copper => "Tooltip_Tier_Copper",
        ToolTier.Iron => "Tooltip_Tier_Iron",
        _ => "Tooltip_Tier_Basic",
    };
}

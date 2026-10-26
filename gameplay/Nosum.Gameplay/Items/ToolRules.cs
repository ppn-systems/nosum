// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;

namespace Nosum.Gameplay.Items;

/// <summary>Whether an area use (a line of tiles worked with one swing) is allowed.</summary>
public enum AreaVerdict
{
    /// <summary>The use is allowed.</summary>
    Ok = 0,

    /// <summary>The held item cannot work an area (only a hoe and a watering can can).</summary>
    NotAnAreaTool = 1,

    /// <summary>The held tool's tier is lower than the line asked for.</summary>
    TooWide = 2,

    /// <summary>The length is not one of the lengths a tool can have.</summary>
    InvalidLength = 3,
}

/// <summary>
/// What a tool's tier changes (docs/adr/ADR-026 D1): damage per hit for the axe and the pickaxe, the line a hoe or a watering can works
/// when the use is wide, and how much water a can holds. Pure; the server decides with it and the client only predicts.
/// </summary>
public static class ToolRules
{
    /// <summary>Gets the highest tier there is.</summary>
    public const ToolTier TopTier = ToolTier.Iron;

    /// <summary>Returns how many hits' worth of damage one swing of an axe or pickaxe does: the tier plus one.</summary>
    /// <param name="tier">The tool's tier.</param>
    /// <returns>Damage per swing, from 1 (basic) to 3 (iron).</returns>
    public static int DamagePerHit(ToolTier tier) => (int)tier + 1;

    /// <summary>Returns the longest line a hoe or a watering can of a tier works: 1, 3 or 5 tiles.</summary>
    /// <param name="tier">The tool's tier.</param>
    /// <returns>The length in tiles.</returns>
    public static int AreaLength(ToolTier tier) => (2 * (int)tier) + 1;

    /// <summary>Returns how much water a watering can of a tier holds: 40, 55 or 70.</summary>
    /// <param name="tier">The can's tier.</param>
    /// <returns>The capacity.</returns>
    public static int WateringCanCapacity(ToolTier tier) => tier switch
    {
        ToolTier.Copper => 55,
        ToolTier.Iron => 70,
        _ => 40,
    };

    /// <summary>Returns the tier of an item; basic for anything that is not an upgraded tool.</summary>
    /// <param name="itemId">The item id.</param>
    /// <returns>The tier.</returns>
    public static ToolTier TierOf(int itemId) => ItemCatalog.TryGet(itemId, out ItemDefinition? item) ? item.Tier : ToolTier.Basic;

    /// <summary>Returns the item id of a tool at a tier.</summary>
    /// <param name="tool">The tool.</param>
    /// <param name="tier">The tier.</param>
    /// <param name="itemId">The item id when the catalogue has that tool at that tier.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public static bool TryGetItem(ToolKind tool, ToolTier tier, out int itemId)
    {
        foreach (ItemDefinition item in ItemCatalog.All)
        {
            if (item.Kind == ItemKind.Tool && item.Tool == tool && item.Tier == tier)
            {
                itemId = item.Id;
                return true;
            }
        }

        itemId = 0;
        return false;
    }

    /// <summary>Returns whether a tool kind can be upgraded at the blacksmith. The scythe has a single tier.</summary>
    /// <param name="tool">The tool kind.</param>
    /// <returns><see langword="true"/> for the hoe, the watering can, the axe and the pickaxe.</returns>
    public static bool IsUpgradable(ToolKind tool) => tool is ToolKind.Hoe or ToolKind.WateringCan or ToolKind.Axe or ToolKind.Pickaxe;

    /// <summary>Checks an area use of a held item against the item's tier. A single tile (length one) is every tool's normal use.</summary>
    /// <param name="held">The held item, or <see langword="null"/> for empty hands.</param>
    /// <param name="length">How many tiles in a line the client asked for.</param>
    /// <returns>The verdict.</returns>
    public static AreaVerdict ValidateArea(ItemDefinition? held, int length)
    {
        if (length is not (1 or 3 or 5))
        {
            return AreaVerdict.InvalidLength;
        }

        if (length == 1)
        {
            return AreaVerdict.Ok;
        }

        if (held is null || held.Kind != ItemKind.Tool || held.Tool is not (ToolKind.Hoe or ToolKind.WateringCan))
        {
            return AreaVerdict.NotAnAreaTool;
        }

        return length <= AreaLength(held.Tier) ? AreaVerdict.Ok : AreaVerdict.TooWide;
    }

    /// <summary>Returns the tiles of a line: the first tile and the ones after it, going the way the farmer faces.</summary>
    /// <param name="tileX">The first tile's x (the tile in front of the farmer).</param>
    /// <param name="tileY">The first tile's y.</param>
    /// <param name="facing">Where the farmer faces.</param>
    /// <param name="length">The number of tiles, at least one.</param>
    /// <param name="index">Which tile of the line, from zero.</param>
    /// <returns>The tile.</returns>
    public static (int X, int Y) LineTile(int tileX, int tileY, Facing facing, int length, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, length);

        return facing switch
        {
            Facing.Up => (tileX, tileY - index),
            Facing.Left => (tileX - index, tileY),
            Facing.Right => (tileX + index, tileY),
            _ => (tileX, tileY + index),
        };
    }
}

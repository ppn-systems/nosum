// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Coops;

/// <summary>The two things a co-op can upgrade with its warehouse stock (docs/adr/ADR-038).</summary>
public enum CoopTrack
{
    /// <summary>How many item kinds the warehouse can hold.</summary>
    Warehouse = 1,

    /// <summary>How much of the co-op land may be tilled and built on.</summary>
    Land = 2,
}

/// <summary>Why an upgrade was refused by the rules.</summary>
public enum UpgradeVerdict
{
    /// <summary>Allowed.</summary>
    Ok = 0,

    /// <summary>The track is not one of <see cref="CoopTrack"/>.</summary>
    UnknownTrack = 1,

    /// <summary>The request names a level that is not the next one: already bought, or a level skipped.</summary>
    NotNextLevel = 2,

    /// <summary>The track is already at <see cref="CoopUpgrades.MaxLevel"/>.</summary>
    MaxLevel = 3,

    /// <summary>The warehouse does not hold the price.</summary>
    NotEnoughStock = 4,
}

/// <summary>One item of an upgrade's price, taken from the warehouse.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">How many.</param>
public readonly record struct UpgradeCost(int ItemId, int Quantity);

/// <summary>A rectangle of land tiles: <c>X</c>, <c>Y</c> are the top-left tile, <c>Width</c> and <c>Height</c> are in tiles.</summary>
/// <param name="X">Left tile.</param>
/// <param name="Y">Top tile.</param>
/// <param name="Width">Width in tiles.</param>
/// <param name="Height">Height in tiles.</param>
public readonly record struct LandRect(int X, int Y, int Width, int Height)
{
    /// <summary>Returns whether a tile is inside the rectangle.</summary>
    /// <param name="x">Tile x.</param>
    /// <param name="y">Tile y.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool Contains(int x, int y) => x >= this.X && y >= this.Y && x < this.X + this.Width && y < this.Y + this.Height;
}

/// <summary>
/// The pure rules of co-op upgrades (docs/adr/ADR-038): the price table, the warehouse capacity of each level and the part of the
/// co-op land each level unlocks. Numbers are defaults (open owner questions), not balance. The client uses the same land rectangles to
/// shade the locked ground; the server decides.
/// </summary>
public static class CoopUpgrades
{
    /// <summary>The highest level of either track. Level 0 is what every co-op starts with.</summary>
    public const int MaxLevel = 3;

    /// <summary>How many item kinds a level-0 warehouse holds.</summary>
    public const int BaseSlots = 20;

    /// <summary>The audit reason of the warehouse stock spent on a warehouse upgrade.</summary>
    public const string WarehouseReason = "coop.upgrade.warehouse";

    /// <summary>The audit reason of the warehouse stock spent on a land upgrade.</summary>
    public const string LandReason = "coop.upgrade.land";

    private static readonly int[] s_slots = [BaseSlots, 30, 40, 60];

    // Prices of reaching level 1, 2 and 3 (index = target level - 1).
    private static readonly UpgradeCost[][] s_warehouseCosts =
    [
        [new(ItemCatalog.Wood, 50), new(ItemCatalog.Stone, 25)],
        [new(ItemCatalog.Wood, 150), new(ItemCatalog.Stone, 100), new(ItemCatalog.CopperBar, 5)],
        [new(ItemCatalog.Wood, 300), new(ItemCatalog.Stone, 250), new(ItemCatalog.CopperBar, 10), new(ItemCatalog.IronBar, 5)],
    ];

    private static readonly UpgradeCost[][] s_landCosts =
    [
        [new(ItemCatalog.Wood, 100), new(ItemCatalog.Stone, 50)],
        [new(ItemCatalog.Wood, 250), new(ItemCatalog.Stone, 150), new(ItemCatalog.CopperBar, 8)],
        [new(ItemCatalog.Wood, 500), new(ItemCatalog.Stone, 400), new(ItemCatalog.IronBar, 10)],
    ];

    // The ground of the co-op land map (Farm_Foraging.tmx, 80 x 65): level 0 is the north-west meadow around the arrival tile, each level adds
    // one more block, and the last one is the whole map, so nothing is left locked at the top level.
    private static readonly LandRect[] s_landSteps =
    [
        new(19, 8, 29, 29),
        new(19, 37, 29, 24),
        new(48, 17, 32, 29),
        new(0, 0, 80, 65),
    ];

    /// <summary>How many item kinds a warehouse of a level can hold.</summary>
    /// <param name="level">The warehouse level (clamped to 0 to <see cref="MaxLevel"/>).</param>
    /// <returns>The capacity, in item kinds.</returns>
    public static int WarehouseSlots(int level) => s_slots[Math.Clamp(level, 0, MaxLevel)];

    /// <summary>
    /// Checks that a deposit fits: an item the warehouse already holds always fits (capacity counts kinds, not quantity); a new kind needs a free slot.
    /// A warehouse already over its capacity (it was filled before capacity existed) only refuses new kinds.
    /// </summary>
    /// <param name="level">The warehouse level.</param>
    /// <param name="kindsHeld">How many item kinds the warehouse holds now.</param>
    /// <param name="alreadyHeld">Whether the item being deposited is one of them.</param>
    /// <returns><see langword="true"/> when the deposit fits.</returns>
    public static bool CanStore(int level, int kindsHeld, bool alreadyHeld) => alreadyHeld || kindsHeld < WarehouseSlots(level);

    /// <summary>The price of reaching a level of a track from the level below it.</summary>
    /// <param name="track">The track.</param>
    /// <param name="targetLevel">The level to buy, 1 to <see cref="MaxLevel"/>.</param>
    /// <returns>The items to take from the warehouse; empty for a level that cannot be bought.</returns>
    public static IReadOnlyList<UpgradeCost> Cost(CoopTrack track, int targetLevel)
    {
        if (targetLevel < 1 || targetLevel > MaxLevel)
        {
            return [];
        }

        return track switch
        {
            CoopTrack.Warehouse => s_warehouseCosts[targetLevel - 1],
            CoopTrack.Land => s_landCosts[targetLevel - 1],
            _ => [],
        };
    }

    /// <summary>The audit reason of an upgrade of a track.</summary>
    /// <param name="track">The track.</param>
    /// <returns><see cref="WarehouseReason"/> or <see cref="LandReason"/>.</returns>
    public static string ReasonOf(CoopTrack track) => track == CoopTrack.Warehouse ? WarehouseReason : LandReason;

    /// <summary>Checks an upgrade against the current level and the warehouse stock.</summary>
    /// <param name="track">The track.</param>
    /// <param name="currentLevel">The track's level now.</param>
    /// <param name="targetLevel">The level the buyer asked for; it must be exactly the next one, so a repeated request pays once.</param>
    /// <param name="stockOf">How many of an item the warehouse holds.</param>
    /// <returns><see cref="UpgradeVerdict.Ok"/> or the reason.</returns>
    public static UpgradeVerdict CanBuy(CoopTrack track, int currentLevel, int targetLevel, Func<int, int> stockOf)
    {
        ArgumentNullException.ThrowIfNull(stockOf);

        if (track is not (CoopTrack.Warehouse or CoopTrack.Land))
        {
            return UpgradeVerdict.UnknownTrack;
        }

        if (currentLevel >= MaxLevel)
        {
            return UpgradeVerdict.MaxLevel;
        }

        if (targetLevel != currentLevel + 1)
        {
            return UpgradeVerdict.NotNextLevel;
        }

        foreach (UpgradeCost cost in Cost(track, targetLevel))
        {
            if (stockOf(cost.ItemId) < cost.Quantity)
            {
                return UpgradeVerdict.NotEnoughStock;
            }
        }

        return UpgradeVerdict.Ok;
    }

    /// <summary>The rectangle of land that a level unlocks on top of the levels below it (level 0 is the starting area).</summary>
    /// <param name="level">The land level, 0 to <see cref="MaxLevel"/>.</param>
    /// <returns>The rectangle.</returns>
    public static LandRect LandStep(int level) => s_landSteps[Math.Clamp(level, 0, MaxLevel)];

    /// <summary>Returns whether a tile of the co-op land may be tilled or built on at a land level.</summary>
    /// <param name="landLevel">The co-op's land level.</param>
    /// <param name="x">Tile x.</param>
    /// <param name="y">Tile y.</param>
    /// <returns><see langword="true"/> when the tile is unlocked.</returns>
    public static bool IsLandUnlocked(int landLevel, int x, int y)
    {
        int top = Math.Clamp(landLevel, 0, MaxLevel);
        for (int i = 0; i <= top; i++)
        {
            if (s_landSteps[i].Contains(x, y))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns whether a rectangle of tiles (an object's footprint) lies wholly on unlocked land.</summary>
    /// <param name="landLevel">The co-op's land level.</param>
    /// <param name="x">Left tile.</param>
    /// <param name="y">Top tile.</param>
    /// <param name="width">Width in tiles.</param>
    /// <param name="height">Height in tiles.</param>
    /// <returns><see langword="true"/> when every tile is unlocked.</returns>
    public static bool IsAreaUnlocked(int landLevel, int x, int y, int width, int height)
    {
        for (int dy = 0; dy < height; dy++)
        {
            for (int dx = 0; dx < width; dx++)
            {
                if (!IsLandUnlocked(landLevel, x + dx, y + dy))
                {
                    return false;
                }
            }
        }

        return true;
    }
}

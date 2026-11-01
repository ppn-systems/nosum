// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Blacksmith;

/// <summary>What an upgrade costs and gives.</summary>
/// <param name="FromItemId">The tool the player hands over.</param>
/// <param name="ToItemId">The tool they get back.</param>
/// <param name="Tool">The kind of tool.</param>
/// <param name="ToTier">The tier they get back.</param>
/// <param name="Coins">The price in coins.</param>
/// <param name="BarItemId">The kind of bar spent.</param>
/// <param name="BarCount">How many bars.</param>
public readonly record struct UpgradeQuote(int FromItemId, int ToItemId, ToolKind Tool, ToolTier ToTier, long Coins, int BarItemId, int BarCount);

/// <summary>
/// The blacksmith's rules (docs/adr/ADR-026 D2): copper costs 2 000 coins and 5 copper bars, iron 5 000 coins and 5 iron bars, and the
/// tool comes back two game days after it was handed in. Pure; the server prices and times an upgrade with it and the client shows the same
/// numbers.
/// </summary>
public static class BlacksmithRules
{
    /// <summary>Game days between handing a tool in and being able to collect it.</summary>
    public const int UpgradeDays = 2;

    /// <summary>Bars every upgrade takes.</summary>
    public const int BarsPerUpgrade = 5;

    /// <summary>Price of the copper upgrade, coins.</summary>
    public const long CopperCoins = 2_000;

    /// <summary>Price of the iron upgrade, coins.</summary>
    public const long IronCoins = 8_000;

    /// <summary>Quotes the upgrade of a tool to the next tier.</summary>
    /// <param name="toolItemId">The tool item the player hands over; any tier but the top one of an upgradable tool.</param>
    /// <param name="quote">The quote when the tool can be upgraded.</param>
    /// <returns><see langword="true"/> when the item is an upgradable tool below the top tier.</returns>
    public static bool TryQuote(int toolItemId, [NotNullWhen(true)] out UpgradeQuote? quote)
    {
        quote = null;
        if (!ItemCatalog.TryGet(toolItemId, out ItemDefinition? item)
            || item.Kind != ItemKind.Tool
            || !ToolRules.IsUpgradable(item.Tool)
            || item.Tier >= ToolRules.TopTier)
        {
            return false;
        }

        ToolTier to = item.Tier + 1;
        if (!ToolRules.TryGetItem(item.Tool, to, out int toItem))
        {
            return false;
        }

        quote = to == ToolTier.Copper
            ? new UpgradeQuote(item.Id, toItem, item.Tool, to, CopperCoins, ItemCatalog.CopperBar, BarsPerUpgrade)
            : new UpgradeQuote(item.Id, toItem, item.Tool, to, IronCoins, ItemCatalog.IronBar, BarsPerUpgrade);
        return true;
    }

    /// <summary>Returns the game day an upgrade handed in on a day is ready.</summary>
    /// <param name="today">The game day number it is handed in.</param>
    /// <returns>The first day it can be collected.</returns>
    public static long ReadyDay(long today) => today + UpgradeDays;

    /// <summary>Returns whether an upgrade can be collected.</summary>
    /// <param name="readyDay">The day it is ready (<see cref="ReadyDay"/>).</param>
    /// <param name="today">The game day number now.</param>
    /// <returns><see langword="true"/> from the ready day on.</returns>
    public static bool IsReady(long readyDay, long today) => today >= readyDay;
}

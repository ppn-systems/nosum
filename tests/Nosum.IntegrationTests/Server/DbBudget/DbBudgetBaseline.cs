// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Nosum.IntegrationTests.Server.DbBudget;

/// <summary>
/// The most database work a hot-path packet may cost today (docs/adr/ADR-035). A limit is the number measured on the branch that introduced it;
/// the follow-up changes of the ADR lower the numbers, and every lowered number is written down here in the same commit, so a regression
/// fails <see cref="DbBudgetTests"/>. Never raise a limit to make a test pass: a packet that got more expensive is the bug.
/// </summary>
internal static class DbBudgetBaseline
{
    /// <summary>The limits by row key (<c>Name / Variant</c>): reads, writes, transactions, SaveChanges calls.</summary>
    public static IReadOnlyDictionary<string, Limit> HotPaths { get; } = new Dictionary<string, Limit>
    {
        ["BrowseListings / empty market, page 0"] = new(2, 0, 0, 0),
        ["BrowseListings / one listing on the page"] = new(2, 0, 0, 0),
        ["BuyFromShop / buy seeds"] = new(6, 1, 1, 2),
        ["BuyListing / buy one listing"] = new(16, 2, 1, 4),
        ["CancelListing / cancel own listing"] = new(5, 1, 1, 1),
        ["ChestMove / five parsnips into the chest"] = new(11, 1, 1, 4),
        ["ChestMove / one parsnip back out"] = new(11, 1, 1, 4),
        ["Craft / a chest from wood"] = new(2, 1, 1, 1),
        ["Craft / five fences"] = new(2, 1, 1, 1),
        ["CreateListing / first listing"] = new(7, 1, 1, 2),
        ["CreateListing / second listing"] = new(7, 1, 1, 2),
        ["Disconnect / joined player, socket closed"] = new(1, 1, 0, 1),
        ["Eat / a parsnip from the hotbar"] = new(5, 0, 0, 1),
        ["GetCalendar / calendar"] = new(0, 0, 0, 0),
        ["GetFarm / empty farm"] = new(0, 0, 0, 0),
        ["GetGroundItems / nothing on the ground"] = new(0, 0, 0, 0),
        ["GetInventory / starter kit"] = new(2, 0, 0, 1),
        ["GetLooks / own and seen players"] = new(1, 1, 0, 1),
        ["GetMyListings / none"] = new(1, 0, 0, 0),
        ["GetMyListings / one listing"] = new(1, 0, 0, 0),
        ["GetNodes / farm"] = new(0, 0, 0, 0),
        ["GetPlaced / a chest and a furnace standing"] = new(0, 0, 0, 0),
        ["GetPlaced / empty farm"] = new(0, 0, 0, 0),
        ["GetPriceHistory / no sales"] = new(1, 0, 0, 0),
        ["GetQuests / new player"] = new(1, 1, 0, 1),
        ["GetWallet / wallet"] = new(3, 1, 1, 1),
        ["JoinMap / first join, no saved place"] = new(9, 2, 2, 2),
        ["JoinMap / returning player, saved place"] = new(4, 0, 0, 1),
        ["Login / e-mail and password"] = new(1, 1, 1, 1),
        ["MoveIntent / one step, same chunk"] = new(0, 0, 0, 0),
        ["MoveIntent / step inside a chunk, another player in sight"] = new(0, 0, 0, 0),
        ["MoveIntent / step into a new chunk (interest set changes)"] = new(0, 0, 0, 0),
        ["MoveIntent / step onto an exit: change of map"] = new(5, 3, 1, 3),
        ["MoveSlot / split a stack into a free slot"] = new(4, 1, 1, 2),
        ["PickUpObject / pick up a fence"] = new(3, 1, 1, 1),
        ["PlaceObject / a chest"] = new(4, 1, 1, 2),
        ["PlaceObject / a coop blueprint"] = new(7, 2, 1, 2),
        ["PlaceObject / a fence"] = new(4, 1, 1, 2),
        ["PlaceObject / a furnace"] = new(4, 1, 1, 2),
        ["ResumeSession / token, no other session"] = new(2, 1, 0, 1),
        ["ResumeSession / token, replacing an open session"] = new(2, 1, 0, 1),
        ["SelectSlot / hotbar slot 1"] = new(2, 1, 0, 1),
        ["SendChat / local channel"] = new(0, 0, 0, 0),
        ["SendChat / map channel"] = new(1, 0, 0, 0),
        ["SendChat / map channel, one listener has blocked the sender"] = new(1, 0, 0, 0),
        ["SendChat / whisper by nickname"] = new(2, 0, 0, 0),
        ["ShipItems / ship a stack at the bin"] = new(8, 1, 1, 3),
        ["StartGuest / new guest"] = new(1, 1, 1, 1),
        ["TradeConfirm / first side confirms"] = new(0, 0, 0, 0),
        ["TradeConfirm / second side confirms: the swap"] = new(12, 1, 1, 3),
        ["UseItem / break a one-hit node"] = new(8, 2, 2, 3),
        ["UseItem / harvest a mature crop"] = new(10, 4, 2, 5),
        ["UseItem / plant a seed"] = new(6, 2, 1, 3),
        ["UseItem / plant on untilled soil (refused)"] = new(2, 0, 0, 1),
        ["UseItem / slot with nothing in it (refused)"] = new(0, 0, 0, 0),
        ["UseItem / till (hoe)"] = new(4, 1, 1, 2),
        ["UseItem / water"] = new(4, 1, 1, 2),
        ["UseItem / water on a new game day"] = new(4, 1, 1, 2),
    };

    /// <summary>The limit of one row.</summary>
    /// <param name="Reads">Most SELECT commands.</param>
    /// <param name="Writes">Most INSERT/UPDATE/DELETE commands.</param>
    /// <param name="Transactions">Most explicit transactions.</param>
    /// <param name="SaveChanges">Most SaveChanges calls.</param>
    internal readonly record struct Limit(int Reads, int Writes, int Transactions, int SaveChanges);
}

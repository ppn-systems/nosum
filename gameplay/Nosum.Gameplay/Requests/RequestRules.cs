// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Nodes;

namespace Nosum.Gameplay.Requests;

/// <summary>
/// One request of a game day on the Town board (docs/adr/ADR-036 D6): deliver <see cref="Quantity"/> of <see cref="ItemId"/>, get <see cref="RewardCoins"/>.
/// </summary>
/// <param name="Slot">The request's place in its day, from zero; with the day number the only request name that crosses the wire.</param>
/// <param name="ItemId">The item that is delivered.</param>
/// <param name="Quantity">How many items.</param>
/// <param name="RewardCoins">The coins paid for the delivery.</param>
public readonly record struct RequestTemplate(int Slot, int ItemId, int Quantity, long RewardCoins);

/// <summary>
/// Pure rules of the daily requests (docs/adr/ADR-036): which items a season can ask for, the deterministic roll of a day's requests from its number, and
/// the reward. The numbers are product defaults, not balance.
/// </summary>
public static class RequestRules
{
    /// <summary>How many requests the board holds each game day.</summary>
    public const int RequestsPerDay = 3;

    /// <summary>The share added to the base value of a delivery, in percent: filling a request pays better than the shipping bin.</summary>
    public const int PremiumPercent = 30;

    /// <summary>The most items one request asks for.</summary>
    public const int MaxQuantity = 40;

    /// <summary>The share added to a delivery made in the early window of its game day, in percent (docs/adr/ADR-036 D10).</summary>
    public const int EarlyBonusPercent = 20;

    /// <summary>How many in-game hours from the start of a game day the early bonus lasts.</summary>
    public const int EarlyWindowHours = 4;

    // The base value a slot asks for, in coins at the bin price: an easy, a medium and a hard request.
    private static readonly int[] s_valueTargets = [100, 180, 300];

    // Mixed with the day number so that two days never share a stream and day one does not roll a zero seed.
    private const ulong SeedSalt = 0x5245_5155_4553_5453;

    /// <summary>Lists the items a request may ask for in a season: what the crops of the season yield and what is foraged in it, each one the shipping bin prices.</summary>
    /// <param name="season">The season.</param>
    /// <returns>The distinct item ids in ascending order.</returns>
    public static int[] ItemsInSeason(Season season)
    {
        SortedSet<int> items = [];
        foreach (CropDefinition crop in CropCatalog.All)
        {
            if (crop.Seasons.Contains(season) && ShippingPrices.TryGet(crop.HarvestItemId, out _))
            {
                _ = items.Add(crop.HarvestItemId);
            }
        }

        foreach (ForageEntry forage in ForageCatalog.All)
        {
            if (forage.Seasons.Contains(season) && ShippingPrices.TryGet(forage.ItemId, out _))
            {
                _ = items.Add(forage.ItemId);
            }
        }

        int[] result = new int[items.Count];
        items.CopyTo(result);
        return result;
    }

    /// <summary>Rolls the requests of a game day. The same day and season always give the same requests, on any machine and in any order of calls.</summary>
    /// <param name="day">The game day number (<c>GameCalendar.DayNumberAt</c>).</param>
    /// <param name="season">The season the day falls in.</param>
    /// <returns>Up to <see cref="RequestsPerDay"/> requests with distinct items, by slot; fewer only when the season has fewer items.</returns>
    public static RequestTemplate[] Roll(int day, Season season)
    {
        List<int> pool = [.. ItemsInSeason(season)];
        int count = Math.Min(RequestsPerDay, pool.Count);
        RequestTemplate[] requests = new RequestTemplate[count];
        ulong state = ((ulong)(uint)Math.Max(0, day) * 0x9E37_79B9_7F4A_7C15) ^ SeedSalt;
        for (int slot = 0; slot < count; slot++)
        {
            int pick = (int)(Next(ref state) % (ulong)pool.Count);
            int itemId = pool[pick];
            pool.RemoveAt(pick);
            int quantity = QuantityFor(itemId, s_valueTargets[slot % s_valueTargets.Length]);
            requests[slot] = new RequestTemplate(slot, itemId, quantity, RewardFor(itemId, quantity));
        }

        return requests;
    }

    /// <summary>Tells how many of an item a request of a given base value asks for: at least one, never more than <see cref="MaxQuantity"/>.</summary>
    /// <param name="itemId">The item; one the bin does not price asks for one.</param>
    /// <param name="valueTarget">The base value in coins at the bin price.</param>
    /// <returns>The quantity.</returns>
    public static int QuantityFor(int itemId, int valueTarget)
    {
        if (!ShippingPrices.TryGet(itemId, out int each))
        {
            return 1;
        }

        return Math.Clamp(valueTarget / each, 1, MaxQuantity);
    }

    /// <summary>Tells what a delivery pays: the bin price of the items plus <see cref="PremiumPercent"/>, rounded down.</summary>
    /// <param name="itemId">The item; one the bin does not price pays nothing.</param>
    /// <param name="quantity">How many items.</param>
    /// <returns>The coins.</returns>
    public static long RewardFor(int itemId, int quantity)
    {
        if (quantity < 1 || !ShippingPrices.TryGet(itemId, out int each))
        {
            return 0;
        }

        return (long)each * quantity * (100 + PremiumPercent) / 100;
    }

    /// <summary>Tells how long the early window of a game day lasts, in milliseconds: <see cref="EarlyWindowHours"/> of the day's in-game hours.</summary>
    /// <param name="dayLengthMs">The length of a game day in real time.</param>
    /// <returns>The window in milliseconds.</returns>
    public static long EarlyWindowMs(long dayLengthMs) => dayLengthMs * EarlyWindowHours * 60 / GameCalendar.MinutesPerDay;

    /// <summary>Tells whether a moment falls in the early window of its game day.</summary>
    /// <param name="msIntoDay">Milliseconds since the day started (the server clock minus <c>GameCalendar.DayStartMs</c>).</param>
    /// <param name="dayLengthMs">The length of a game day in real time.</param>
    /// <returns><see langword="true"/> from the start of the day until the window ends.</returns>
    public static bool IsEarly(long msIntoDay, long dayLengthMs) => msIntoDay >= 0 && msIntoDay < EarlyWindowMs(dayLengthMs);

    /// <summary>Tells what a delivery pays once the early bonus is applied: the base reward, plus <see cref="EarlyBonusPercent"/> when early, rounded down.</summary>
    /// <param name="reward">The base reward of the request.</param>
    /// <param name="early">Whether the delivery is in the early window.</param>
    /// <returns>The coins paid.</returns>
    public static long PaidFor(long reward, bool early) => early ? reward * (100 + EarlyBonusPercent) / 100 : reward;

    /// <summary>Returns whether a slot number names a request of a day.</summary>
    /// <param name="slot">The slot as sent by a client; not trusted.</param>
    /// <returns><see langword="true"/> for a slot from zero to <see cref="RequestsPerDay"/> minus one.</returns>
    public static bool IsValidSlot(int slot) => slot >= 0 && slot < RequestsPerDay;

    // SplitMix64: a fixed, portable stream (unlike System.Random or string hashing, which may change between runtimes).
    private static ulong Next(ref ulong state)
    {
        state += 0x9E37_79B9_7F4A_7C15;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9;
        z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EB;
        return z ^ (z >> 31);
    }
}

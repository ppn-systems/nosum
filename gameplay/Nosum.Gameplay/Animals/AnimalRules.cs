// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.GroundItems;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Players;

namespace Nosum.Gameplay.Animals;

/// <summary>What an overnight feeding settlement found.</summary>
/// <param name="Rations">Rations left in the feeding bench.</param>
/// <param name="FedToday">How many animals ate at the start of today and so have produce today.</param>
public readonly record struct FeedingSettlement(int Rations, int FedToday);

/// <summary>
/// Rules of farm animals (docs/adr/ADR-028 D3): feeding, produce, petting and friendship. Pure functions of stored numbers and the game day: nothing ticks, and an animal the
/// player never visits costs the server nothing. A game day is the number from <c>GameCalendar.DayNumberAt</c>.
/// </summary>
public static class AnimalRules
{
    /// <summary>Highest friendship.</summary>
    public const int MaxFriendship = 1000;

    /// <summary>Friendship a day's petting adds.</summary>
    public const int PettingGain = 40;

    /// <summary>Friendship per heart: an animal shows one heart per 200, five at the maximum.</summary>
    public const int FriendshipPerHeart = 200;

    /// <summary>Friendship from which an animal can give gold-quality produce (four hearts).</summary>
    public const int GoldFriendship = 4 * FriendshipPerHeart;

    /// <summary>Chance, in percent, that an animal with at least <see cref="GoldFriendship"/> gives gold-quality produce on a day.</summary>
    public const int GoldChancePercent = 25;

    /// <summary>Mixed into the seed of the gold roll so it is independent of the large roll.</summary>
    private const ulong GoldSalt = 0x9E37_79B9_7F4A_7C15;

    /// <summary>Rations the feeding bench holds.</summary>
    public const int MaxRations = 24;

    /// <summary>Largest stack of feed one request puts in the bench.</summary>
    public const int MaxFeedAtOnce = MaxRations;

    /// <summary>How far, in pixels, a player's feet may be from an animal to pet it or collect from it.</summary>
    public const double ReachPixels = 44;

    /// <summary>The marker of an animal that never had produce or was never petted: no game day is that early.</summary>
    public const int Never = 0;

    /// <summary>Tells how many rations an item is worth when put into the feeding bench.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>One for hay and fiber, zero for anything else.</returns>
    public static int RationsOf(int itemId) => itemId is ItemCatalog.Hay or ItemCatalog.Fiber ? 1 : 0;

    /// <summary>Checks a name for an animal: the nickname rules of docs/adr/ADR-009, including the profanity filter.</summary>
    /// <param name="typed">What the player typed.</param>
    /// <returns><see langword="true"/> when the name may be used.</returns>
    public static bool IsValidName(string? typed) => NicknameRules.Validate(typed) == NicknameVerdict.Ok;

    /// <summary>Settles the overnight feeding up to a game day (docs/adr/ADR-028 D3): each night every animal eats one ration if the bench has one, and an animal that ate at the start of a day has produce that day.</summary>
    /// <param name="rations">Rations in the bench after the last settlement.</param>
    /// <param name="animals">Animals living in the building.</param>
    /// <param name="settledDay">The last game day settled.</param>
    /// <param name="today">The game day now.</param>
    /// <returns>The rations left and how many animals ate at the start of <paramref name="today"/>; unchanged when the day was already settled.</returns>
    public static FeedingSettlement Settle(int rations, int animals, int settledDay, int today)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rations);
        ArgumentOutOfRangeException.ThrowIfNegative(animals);

        if (today <= settledDay || animals == 0)
        {
            return new FeedingSettlement(rations, 0);
        }

        // One night per day since the last settlement; the bench empties after at most MaxRations nights, and only the last night decides today's produce.
        long nights = (long)today - settledDay;
        int fed = 0;
        for (long night = 1; night <= nights; night++)
        {
            fed = Math.Min(rations, animals);
            rations -= fed;
            if (rations == 0 && night < nights)
            {
                fed = 0;
                break;
            }
        }

        return new FeedingSettlement(rations, fed);
    }

    /// <summary>Tells whether an animal has produce to collect today.</summary>
    /// <param name="produceDay">The game day the animal last ate at the start of.</param>
    /// <param name="collectedDay">The game day its produce was last collected.</param>
    /// <param name="today">The game day now.</param>
    /// <returns><see langword="true"/> when it produced today and nobody collected it yet.</returns>
    public static bool HasProduce(int produceDay, int collectedDay, int today) => produceDay == today && collectedDay != today;

    /// <summary>Tells whether an animal's produce is due on a game day, from its species' schedule and the weather (docs/adr/ADR-028 amendment 2).</summary>
    /// <param name="schedule">The species' schedule.</param>
    /// <param name="today">The game day.</param>
    /// <param name="sunny">Whether the day is sunny.</param>
    /// <returns><see langword="true"/> when the schedule lets the produce come on that day.</returns>
    public static bool ScheduleAllows(ProduceSchedule schedule, int today, bool sunny) => schedule switch
    {
        ProduceSchedule.EveryThirdDay => today % 3 == 0,
        ProduceSchedule.SunnyDays => sunny,
        _ => true,
    };

    /// <summary>Tells whether an animal has produce to collect today, counting its species' schedule and the weather.</summary>
    /// <param name="definition">The species.</param>
    /// <param name="produceDay">The game day the animal last ate at the start of.</param>
    /// <param name="collectedDay">The game day its produce was last collected.</param>
    /// <param name="today">The game day now.</param>
    /// <param name="sunny">Whether today is sunny.</param>
    /// <returns><see langword="true"/> when <see cref="HasProduce"/> and <see cref="ScheduleAllows"/> both hold.</returns>
    public static bool Yields(AnimalDefinition definition, int produceDay, int collectedDay, int today, bool sunny)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return HasProduce(produceDay, collectedDay, today) && ScheduleAllows(definition.Schedule, today, sunny);
    }

    /// <summary>Tells whether an animal may be petted today.</summary>
    /// <param name="pettedDay">The game day it was last petted.</param>
    /// <param name="today">The game day now.</param>
    /// <returns><see langword="true"/> once a day.</returns>
    public static bool CanPet(int pettedDay, int today) => pettedDay != today;

    /// <summary>Returns the friendship after a petting.</summary>
    /// <param name="friendship">Friendship now.</param>
    /// <returns>Friendship plus <see cref="PettingGain"/>, at most <see cref="MaxFriendship"/>.</returns>
    public static int AfterPetting(int friendship) => Math.Clamp(friendship + PettingGain, 0, MaxFriendship);

    /// <summary>Returns the chance, in percent, that an animal's produce is the large kind: 5 at no friendship up to 55 at the most.</summary>
    /// <param name="friendship">Friendship, zero to <see cref="MaxFriendship"/>.</param>
    /// <returns>The percentage.</returns>
    public static int LargeChancePercent(int friendship) => 5 + (Math.Clamp(friendship, 0, MaxFriendship) / 20);

    /// <summary>Decides, from a seed that depends on the animal and the day only, whether the produce of that day is the large kind. A higher friendship can only turn a small produce large, never the other way.</summary>
    /// <param name="animalId">The animal's id.</param>
    /// <param name="day">The game day.</param>
    /// <param name="friendship">Friendship at the time of collecting.</param>
    /// <returns><see langword="true"/> for the large produce.</returns>
    public static bool IsLarge(Guid animalId, int day, int friendship) => new GameRandom(SeedOf(animalId, day)).Range(0, 99) < LargeChancePercent(friendship);

    /// <summary>Tells how many hearts an animal shows for its friendship: one per <see cref="FriendshipPerHeart"/>, from zero to five.</summary>
    /// <param name="friendship">Friendship, zero to <see cref="MaxFriendship"/>.</param>
    /// <returns>Zero to five.</returns>
    public static int HeartsOf(int friendship) => Math.Clamp(friendship, 0, MaxFriendship) / FriendshipPerHeart;

    /// <summary>Decides, from the same seed as <see cref="IsLarge"/> but a salted roll, whether the produce of that day is gold quality. Only an animal with four hearts or more can give gold, on <see cref="GoldChancePercent"/> of days.</summary>
    /// <param name="animalId">The animal's id.</param>
    /// <param name="day">The game day.</param>
    /// <param name="friendship">Friendship at the time of collecting.</param>
    /// <returns><see langword="true"/> for the gold produce.</returns>
    public static bool IsGold(Guid animalId, int day, int friendship) =>
        friendship >= GoldFriendship && new GameRandom(GameRandom.Mix(SeedOf(animalId, day), GoldSalt)).Range(0, 99) < GoldChancePercent;

    /// <summary>Returns the item an animal gives: gold quality first, then large, then the everyday produce.</summary>
    /// <param name="definition">The species.</param>
    /// <param name="animalId">The animal's id.</param>
    /// <param name="day">The game day.</param>
    /// <param name="friendship">Friendship at the time of collecting.</param>
    /// <returns>The produce, large produce or gold produce item id.</returns>
    public static int ProduceOf(AnimalDefinition definition, Guid animalId, int day, int friendship)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (IsGold(animalId, day, friendship))
        {
            return definition.GoldProduct;
        }

        return IsLarge(animalId, day, friendship) ? definition.LargeProduct : definition.Product;
    }

    // The seed of the day's rolls: depends on the animal and the day only, so asking again cannot re-roll them.
    private static ulong SeedOf(Guid animalId, int day)
    {
        Span<byte> bytes = stackalloc byte[16];
        _ = animalId.TryWriteBytes(bytes);
        return GameRandom.Mix(BitConverter.ToUInt64(bytes), (ulong)day);
    }

    /// <summary>Tells whether a player's feet are close enough to an animal.</summary>
    /// <param name="feetX">Player feet x, pixels.</param>
    /// <param name="feetY">Player feet y, pixels.</param>
    /// <param name="animalX">Animal x, pixels (the middle of its feet).</param>
    /// <param name="animalY">Animal y, pixels.</param>
    /// <returns><see langword="true"/> within <see cref="ReachPixels"/>.</returns>
    public static bool InReach(double feetX, double feetY, double animalX, double animalY)
    {
        double dx = feetX - animalX;
        double dy = feetY - animalY;
        return (dx * dx) + (dy * dy) <= ReachPixels * ReachPixels;
    }
}

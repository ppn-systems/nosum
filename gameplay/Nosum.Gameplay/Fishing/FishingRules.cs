// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Skills;
using Nosum.Gameplay.Weather;

namespace Nosum.Gameplay.Fishing;

/// <summary>What the hook did at the moment the player reeled in (docs/adr/ADR-078 D4).</summary>
public enum HookVerdict : byte
{
    /// <summary>Too early: the fish has not bitten yet. The cast stays open.</summary>
    TooEarly = 0,

    /// <summary>In the window: the fish is hooked and the roll decides what it is.</summary>
    Hooked = 1,

    /// <summary>Too late: the fish got away and the cast is over.</summary>
    Escaped = 2,
}

/// <summary>The conditions a cast is rolled under: where, when and how skilled (docs/adr/ADR-078 D3).</summary>
/// <param name="Water">The water the line is cast into.</param>
/// <param name="Season">The season now.</param>
/// <param name="Weather">The weather now.</param>
/// <param name="Hour">The game hour now, 0 to 23.</param>
/// <param name="Level">The fishing level, 0 to <see cref="SkillRules.MaxLevel"/>.</param>
public readonly record struct FishingSpot(FishingWater Water, Season Season, WeatherKind Weather, int Hour, int Level);

/// <summary>
/// The fishing rules (docs/adr/ADR-078). Pure: the server passes the clock readings and the seed, the client runs the same rules to
/// show the bite window. The server decides every catch from its own timestamps and a seed it made itself, so a client can neither
/// choose the fish nor reel in early or late by reporting its own time.
/// </summary>
public static class FishingRules
{
    /// <summary>Energy one cast costs.</summary>
    public const int CastEnergy = 10;

    /// <summary>The first game hour of the day (inclusive).</summary>
    public const int DawnHour = 6;

    /// <summary>The first game hour of the night (inclusive).</summary>
    public const int DuskHour = 19;

    /// <summary>The shortest wait before a bite, in milliseconds.</summary>
    public const int BiteMinMs = 2000;

    /// <summary>The spread of the bite wait above <see cref="BiteMinMs"/>, in milliseconds.</summary>
    public const int BiteSpreadMs = 4000;

    /// <summary>The longest press window at level zero, in milliseconds.</summary>
    public const int WindowMaxMs = 1000;

    /// <summary>The shortest press window, at the top level, in milliseconds.</summary>
    public const int WindowMinMs = 500;

    /// <summary>How much earlier than the bite the server still accepts a press, to absorb network delay, in milliseconds.</summary>
    public const int EarlyToleranceMs = 300;

    /// <summary>How much later than the window the server still accepts a press, to absorb network delay, in milliseconds.</summary>
    public const int LateToleranceMs = 400;

    /// <summary>How far a player may stand from the water tile they cast at, in tiles (centre to feet). Docs/adr/ADR-078 D1.</summary>
    public const int CastReachTiles = 3;

    /// <summary>How long an open cast waits for its reel-in before it is dropped, in milliseconds (docs/adr/ADR-078 D7).</summary>
    public const int CastLifetimeMs = 60_000;

    /// <summary>Chance, in permille, that a cast brings nothing up at level zero.</summary>
    public const int BaseNothingPermille = 300;

    /// <summary>Lowest chance, in permille, that a cast brings nothing up.</summary>
    public const int MinNothingPermille = 80;

    /// <summary>How much the chance of nothing falls per fishing level, in permille.</summary>
    public const int NothingPermillePerLevel = 15;

    /// <summary>How much a rare fish's weight grows per fishing level, in permille of its weight (the rarity tier multiplies it).</summary>
    public const int RarityPermillePerLevel = 60;

    private const int StreamBite = 1;
    private const int StreamRoll = 2;
    private const int StreamSize = 3;

    /// <summary>
    /// Balance data: the size range of a caught fish in centimetres, inclusive, by rarity (index = <see cref="FishRarity"/> value).
    /// Change these numbers to rebalance; the code reads them, nothing else states them.
    /// </summary>
    private static readonly (int Min, int Max)[] s_sizeRangeCm =
    [
        (10, 30),   // Common
        (20, 45),   // Uncommon
        (40, 70),   // Rare
        (60, 100),  // Legendary
    ];

    /// <summary>Gets the part of the day for a game hour.</summary>
    /// <param name="hour">Game hour, 0 to 23.</param>
    /// <returns><see cref="FishTime.Day"/> from dawn to dusk, otherwise <see cref="FishTime.Night"/>.</returns>
    public static FishTime TimeOf(int hour) => hour >= DawnHour && hour < DuskHour ? FishTime.Day : FishTime.Night;

    /// <summary>Makes the seed of one cast from values the server chose, so no client can pick it.</summary>
    /// <param name="playerKey">A stable key of the player (the first 64 bits of the account id).</param>
    /// <param name="castId">The cast id the server handed out.</param>
    /// <param name="castAtMs">The server clock when the line was cast, in Unix milliseconds.</param>
    /// <param name="tileX">The tile the line was cast at.</param>
    /// <param name="tileY">The tile the line was cast at.</param>
    /// <returns>The seed.</returns>
    public static ulong SeedOf(ulong playerKey, uint castId, long castAtMs, int tileX, int tileY)
        => NodeRandom.Mix(NodeRandom.Mix(playerKey, castId, castAtMs), tileX, tileY);

    /// <summary>Gets how long after the cast the fish bites.</summary>
    /// <param name="seed">The cast seed.</param>
    /// <returns>The wait in milliseconds, from <see cref="BiteMinMs"/> to <see cref="BiteMinMs"/> + <see cref="BiteSpreadMs"/> - 1.</returns>
    public static int BiteDelayMs(ulong seed)
    {
        NodeRandom bite = new(NodeRandom.Mix(seed, StreamBite, 0));
        return BiteMinMs + bite.Next(BiteSpreadMs);
    }

    /// <summary>Rolls the size of a caught fish, in centimetres, inside its rarity's range (balance data in <c>s_sizeRangeCm</c>).</summary>
    /// <param name="rarity">The rarity of the fish caught.</param>
    /// <param name="seed">The cast seed.</param>
    /// <returns>The size in centimetres, inclusive of the rarity's minimum and maximum.</returns>
    public static int SizeCm(FishRarity rarity, ulong seed)
    {
        (int min, int max) = s_sizeRangeCm[(int)rarity];
        NodeRandom size = new(NodeRandom.Mix(seed, StreamSize, 0));
        return min + size.Next(max - min + 1);
    }

    /// <summary>Gets how long the press window is; a better fisher has a shorter one.</summary>
    /// <param name="level">The fishing level, 0 to <see cref="SkillRules.MaxLevel"/>.</param>
    /// <returns>The window in milliseconds, from <see cref="WindowMinMs"/> to <see cref="WindowMaxMs"/>.</returns>
    public static int WindowMs(int level)
    {
        int clamped = Math.Clamp(level, 0, SkillRules.MaxLevel);
        return WindowMaxMs - ((WindowMaxMs - WindowMinMs) * clamped / SkillRules.MaxLevel);
    }

    /// <summary>Judges a reel-in against the bite. The server times it on its own clock from the cast.</summary>
    /// <param name="elapsedMs">Milliseconds from the cast to the reel-in, on the server clock.</param>
    /// <param name="biteDelayMs">The bite wait of the cast.</param>
    /// <param name="windowMs">The press window of the cast.</param>
    /// <returns>Too early, hooked, or escaped.</returns>
    public static HookVerdict Judge(long elapsedMs, int biteDelayMs, int windowMs)
    {
        if (elapsedMs < biteDelayMs - EarlyToleranceMs)
        {
            return HookVerdict.TooEarly;
        }

        return elapsedMs > (long)biteDelayMs + windowMs + LateToleranceMs ? HookVerdict.Escaped : HookVerdict.Hooked;
    }

    /// <summary>Tells whether a player may cast at a tile from where their feet are (docs/adr/ADR-078 D1).</summary>
    /// <param name="feetX">Feet x in world pixels.</param>
    /// <param name="feetY">Feet y in world pixels.</param>
    /// <param name="tileX">Target tile x.</param>
    /// <param name="tileY">Target tile y.</param>
    /// <param name="tileSize">Pixels per tile.</param>
    /// <returns><see langword="true"/> when the tile centre is within <see cref="CastReachTiles"/> tiles.</returns>
    public static bool InCastReach(double feetX, double feetY, int tileX, int tileY, int tileSize)
    {
        double dx = ((tileX + 0.5) * tileSize) - feetX;
        double dy = ((tileY + 0.5) * tileSize) - feetY;
        double reach = (double)CastReachTiles * tileSize;
        return (dx * dx) + (dy * dy) <= reach * reach;
    }

    /// <summary>Rolls what a hooked line brings up. The same spot and seed always give the same result.</summary>
    /// <param name="spot">The conditions of the cast.</param>
    /// <param name="seed">The cast seed.</param>
    /// <returns>The species, or <see langword="null"/> when the line brings up nothing but weeds.</returns>
    public static FishSpecies? Roll(FishingSpot spot, ulong seed)
    {
        int level = Math.Clamp(spot.Level, 0, SkillRules.MaxLevel);
        List<FishSpecies> eligible = Eligible(spot);
        if (eligible.Count == 0)
        {
            return null;
        }

        NodeRandom roll = new(NodeRandom.Mix(seed, StreamRoll, 0));
        int nothing = Math.Max(MinNothingPermille, BaseNothingPermille - (NothingPermillePerLevel * level));
        if (roll.Chance(nothing))
        {
            return null;
        }

        long total = 0;
        long[] weights = new long[eligible.Count];
        for (int i = 0; i < eligible.Count; i++)
        {
            weights[i] = WeightOf(eligible[i], level);
            total += weights[i];
        }

        long pick = roll.Next((int)Math.Min(int.MaxValue, total));
        for (int i = 0; i < eligible.Count; i++)
        {
            if (pick < weights[i])
            {
                return eligible[i];
            }

            pick -= weights[i];
        }

        return eligible[^1];
    }

    /// <summary>Gets the experience a catch gives; a line that brings up nothing still gives a little for the attempt.</summary>
    /// <param name="species">The fish, or <see langword="null"/> for nothing.</param>
    /// <returns>The experience.</returns>
    public static int CatchXp(FishSpecies? species) => species is null ? 1 : species.Rarity switch
    {
        FishRarity.Common => 5,
        FishRarity.Uncommon => 10,
        FishRarity.Rare => 20,
        _ => 40,
    };

    private static List<FishSpecies> Eligible(FishingSpot spot)
    {
        FishTime time = TimeOf(spot.Hour);
        List<FishSpecies> eligible = [];
        foreach (FishSpecies species in FishCatalog.All)
        {
            if ((species.Water & spot.Water) == FishingWater.None
                || !species.Seasons.Contains(spot.Season)
                || (species.Time != FishTime.Any && species.Time != time)
                || (species.Weather == FishWeather.Waters && !WeatherRules.Waters(spot.Weather)))
            {
                continue;
            }

            eligible.Add(species);
        }

        return eligible;
    }

    private static long WeightOf(FishSpecies species, int level)
        => (long)species.Weight * (1000 + (RarityPermillePerLevel * (int)species.Rarity * level)) / 1000;
}

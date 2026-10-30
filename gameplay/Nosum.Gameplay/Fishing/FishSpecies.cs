// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Weather;

namespace Nosum.Gameplay.Fishing;

/// <summary>How rare a fish is. Rarity sets how often it is rolled, how much it is worth to fish for and the experience a catch gives.</summary>
public enum FishRarity : byte
{
    /// <summary>Caught all the time.</summary>
    Common = 0,

    /// <summary>Caught now and then.</summary>
    Uncommon = 1,

    /// <summary>Caught rarely; the weight grows with the fishing level.</summary>
    Rare = 2,

    /// <summary>Caught once in a long while; the weight grows fastest with the fishing level.</summary>
    Legendary = 3,
}

/// <summary>Which part of the game day a fish bites in.</summary>
public enum FishTime : byte
{
    /// <summary>Any time.</summary>
    Any = 0,

    /// <summary>Between dawn and dusk (<see cref="FishingRules.DawnHour"/> to <see cref="FishingRules.DuskHour"/>).</summary>
    Day = 1,

    /// <summary>After dusk and before dawn.</summary>
    Night = 2,
}

/// <summary>Which weather a fish bites in.</summary>
public enum FishWeather : byte
{
    /// <summary>Any weather.</summary>
    Any = 0,

    /// <summary>Only when it rains or storms (<see cref="WeatherRules.Waters"/>).</summary>
    Waters = 1,
}

/// <summary>
/// One fish species (docs/adr/ADR-078 D3). The id is the item id of the fish, so a catch is an ordinary inventory item. Balance data:
/// the numbers may change without an ADR.
/// </summary>
public sealed class FishSpecies
{
    /// <summary>Initializes a species.</summary>
    /// <param name="itemId">The item id of the fish.</param>
    /// <param name="key">Stable key, for example <c>sea_bass</c>; names come from the localization catalogue.</param>
    /// <param name="rarity">How rare the fish is.</param>
    /// <param name="weight">Relative weight in the roll before the rarity and level bonus; greater is more common.</param>
    /// <param name="seasons">The seasons it is caught in.</param>
    /// <param name="water">The waters it lives in.</param>
    /// <param name="time">The part of the day it bites in.</param>
    /// <param name="weather">The weather it bites in.</param>
    public FishSpecies(int itemId, string key, FishRarity rarity, int weight, SeasonSet seasons, FishingWater water, FishTime time = FishTime.Any, FishWeather weather = FishWeather.Any)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weight);
        ArgumentOutOfRangeException.ThrowIfEqual(water, FishingWater.None);
        this.ItemId = itemId;
        this.Key = key;
        this.Rarity = rarity;
        this.Weight = weight;
        this.Seasons = seasons;
        this.Water = water;
        this.Time = time;
        this.Weather = weather;
    }

    /// <summary>Gets the item id of the fish.</summary>
    public int ItemId { get; }

    /// <summary>Gets the stable key.</summary>
    public string Key { get; }

    /// <summary>Gets how rare the fish is.</summary>
    public FishRarity Rarity { get; }

    /// <summary>Gets the relative weight before the rarity and level bonus.</summary>
    public int Weight { get; }

    /// <summary>Gets the seasons it is caught in.</summary>
    public SeasonSet Seasons { get; }

    /// <summary>Gets the waters it lives in.</summary>
    public FishingWater Water { get; }

    /// <summary>Gets the part of the day it bites in.</summary>
    public FishTime Time { get; }

    /// <summary>Gets the weather it bites in.</summary>
    public FishWeather Weather { get; }
}

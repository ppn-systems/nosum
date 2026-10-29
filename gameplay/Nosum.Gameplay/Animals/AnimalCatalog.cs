// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Animals;

/// <summary>The species of farm animals (docs/adr/ADR-028 D3). Values are stored in the database and sent on the wire: append only, never renumber.</summary>
public enum AnimalKind : byte
{
    /// <summary>Not an animal.</summary>
    None = 0,

    /// <summary>A chicken: lays eggs, lives in a coop.</summary>
    Chicken = 1,

    /// <summary>A cow: gives milk to a pail, lives in a barn.</summary>
    Cow = 2,

    /// <summary>A duck: lays duck eggs, lives in a coop.</summary>
    Duck = 3,

    /// <summary>A goat: gives goat milk to a pail, lives in a barn.</summary>
    Goat = 4,

    /// <summary>A sheep: grows wool, shorn once every third game day, lives in a barn.</summary>
    Sheep = 5,

    /// <summary>A pig: digs up truffles on sunny days, lives in a barn.</summary>
    Pig = 6,
}

/// <summary>When a species gives its produce, on top of having been fed the night before (docs/adr/ADR-028 amendment 2). Values are stored in the definitions only: append only.</summary>
public enum ProduceSchedule : byte
{
    /// <summary>Every game day.</summary>
    Daily = 0,

    /// <summary>Every third game day: the day number divides by three.</summary>
    EveryThirdDay = 1,

    /// <summary>Only on a sunny game day.</summary>
    SunnyDays = 2,
}

/// <summary>One species as data: price, produce and the early sheet it is drawn from.</summary>
public sealed class AnimalDefinition
{
    /// <summary>Initializes a definition.</summary>
    /// <param name="kind">The species.</param>
    /// <param name="key">Stable key, also the localization key suffix.</param>
    /// <param name="coins">What the carpenter charges.</param>
    /// <param name="product">The everyday produce item.</param>
    /// <param name="largeProduct">The large produce item a well-loved animal gives more often.</param>
    /// <param name="goldProduct">The gold-quality produce item an animal with four hearts or more sometimes gives.</param>
    /// <param name="needsPail">Whether collecting the produce takes a milk pail in hand.</param>
    /// <param name="schedule">On which days the produce comes.</param>
    /// <param name="sheet">File in <c>sprites/animals/</c> without extension (early art, docs/adr/ADR-007).</param>
    /// <param name="frameWidth">Width of one frame, pixels.</param>
    /// <param name="frameHeight">Height of one frame, pixels.</param>
    /// <param name="walkPixelsPerSecond">How fast it walks inside its building.</param>
    public AnimalDefinition(AnimalKind kind, string key, long coins, int product, int largeProduct, int goldProduct, bool needsPail, ProduceSchedule schedule, string sheet, int frameWidth, int frameHeight, int walkPixelsPerSecond)
    {
        this.Kind = kind;
        this.Key = key;
        this.Coins = coins;
        this.Product = product;
        this.LargeProduct = largeProduct;
        this.GoldProduct = goldProduct;
        this.NeedsPail = needsPail;
        this.Schedule = schedule;
        this.Sheet = sheet;
        this.FrameWidth = frameWidth;
        this.FrameHeight = frameHeight;
        this.WalkPixelsPerSecond = walkPixelsPerSecond;
    }

    /// <summary>Gets the species.</summary>
    public AnimalKind Kind { get; }

    /// <summary>Gets the stable key.</summary>
    public string Key { get; }

    /// <summary>Gets the price in coins.</summary>
    public long Coins { get; }

    /// <summary>Gets the everyday produce item.</summary>
    public int Product { get; }

    /// <summary>Gets the large produce item.</summary>
    public int LargeProduct { get; }

    /// <summary>Gets the gold-quality produce item, given to an animal with four hearts or more.</summary>
    public int GoldProduct { get; }

    /// <summary>Gets whether collecting takes a milk pail.</summary>
    public bool NeedsPail { get; }

    /// <summary>Gets on which days the produce comes.</summary>
    public ProduceSchedule Schedule { get; }

    /// <summary>Gets the sheet: a file in <c>sprites/animals/</c>, without extension.</summary>
    public string Sheet { get; }

    /// <summary>Gets the width of one sheet frame, pixels. Rows are down, right, up; four walking frames each; left is the right row mirrored.</summary>
    public int FrameWidth { get; }

    /// <summary>Gets the height of one sheet frame, pixels.</summary>
    public int FrameHeight { get; }

    /// <summary>Gets how fast it walks inside its building, pixels per second.</summary>
    public int WalkPixelsPerSecond { get; }
}

/// <summary>The animals of the game (docs/adr/ADR-028 D3 and amendment 2): a chicken (800 coins), a duck (1 100), a cow (1 500), a sheep (1 600), a goat (1 800) and a pig (2 000).</summary>
public static class AnimalCatalog
{
    private static readonly AnimalDefinition[] s_all =
    [
        new(AnimalKind.Chicken, "chicken", 800, ItemCatalog.Egg, ItemCatalog.LargeEgg, ItemCatalog.GoldenEgg, false, ProduceSchedule.Daily, "chicken", 16, 16, 22),
        new(AnimalKind.Cow, "cow", 1_500, ItemCatalog.Milk, ItemCatalog.LargeMilk, ItemCatalog.CreamMilk, true, ProduceSchedule.Daily, "cow", 32, 32, 14),
        new(AnimalKind.Duck, "duck", 1_100, ItemCatalog.DuckEgg, ItemCatalog.LargeDuckEgg, ItemCatalog.GoldenDuckEgg, false, ProduceSchedule.Daily, "duck", 16, 16, 24),
        new(AnimalKind.Goat, "goat", 1_800, ItemCatalog.GoatMilk, ItemCatalog.LargeGoatMilk, ItemCatalog.CreamGoatMilk, true, ProduceSchedule.Daily, "goat", 16, 16, 18),
        new(AnimalKind.Sheep, "sheep", 1_600, ItemCatalog.Wool, ItemCatalog.FineWool, ItemCatalog.GoldenFleece, false, ProduceSchedule.EveryThirdDay, "sheep", 16, 16, 16),
        new(AnimalKind.Pig, "pig", 2_000, ItemCatalog.Truffle, ItemCatalog.LargeTruffle, ItemCatalog.GoldenTruffle, false, ProduceSchedule.SunnyDays, "pig", 16, 16, 20),
    ];

    /// <summary>Gets every species.</summary>
    public static IReadOnlyList<AnimalDefinition> All => s_all;

    /// <summary>Looks up a species.</summary>
    /// <param name="kind">The species.</param>
    /// <param name="definition">The definition when known.</param>
    /// <returns><see langword="true"/> when the kind is an animal.</returns>
    public static bool TryGet(AnimalKind kind, [NotNullWhen(true)] out AnimalDefinition? definition)
    {
        foreach (AnimalDefinition candidate in s_all)
        {
            if (candidate.Kind == kind)
            {
                definition = candidate;
                return true;
            }
        }

        definition = null;
        return false;
    }
}

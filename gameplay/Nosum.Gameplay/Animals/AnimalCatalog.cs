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
}

/// <summary>One species as data: price, produce and the placeholder sheet it is drawn from.</summary>
public sealed class AnimalDefinition
{
    /// <summary>Initializes a definition.</summary>
    /// <param name="kind">The species.</param>
    /// <param name="key">Stable key, also the localization key suffix.</param>
    /// <param name="coins">What the carpenter charges.</param>
    /// <param name="product">The everyday produce item.</param>
    /// <param name="largeProduct">The large produce item a well-loved animal gives more often.</param>
    /// <param name="needsPail">Whether collecting the produce takes a milk pail in hand.</param>
    /// <param name="sheet">File in <c>Animals/</c> without extension (placeholder art, docs/adr/ADR-007).</param>
    /// <param name="frameWidth">Width of one frame, pixels.</param>
    /// <param name="frameHeight">Height of one frame, pixels.</param>
    /// <param name="walkPixelsPerSecond">How fast it walks inside its building.</param>
    public AnimalDefinition(AnimalKind kind, string key, long coins, int product, int largeProduct, bool needsPail, string sheet, int frameWidth, int frameHeight, int walkPixelsPerSecond)
    {
        this.Kind = kind;
        this.Key = key;
        this.Coins = coins;
        this.Product = product;
        this.LargeProduct = largeProduct;
        this.NeedsPail = needsPail;
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

    /// <summary>Gets whether collecting takes a milk pail.</summary>
    public bool NeedsPail { get; }

    /// <summary>Gets the sheet: a file in <c>Animals/</c>, without extension.</summary>
    public string Sheet { get; }

    /// <summary>Gets the width of one sheet frame, pixels. Rows are down, right, up; four walking frames each; left is the right row mirrored.</summary>
    public int FrameWidth { get; }

    /// <summary>Gets the height of one sheet frame, pixels.</summary>
    public int FrameHeight { get; }

    /// <summary>Gets how fast it walks inside its building, pixels per second.</summary>
    public int WalkPixelsPerSecond { get; }
}

/// <summary>The animals of the game: a white chicken (800 coins) and a white cow (1 500 coins).</summary>
public static class AnimalCatalog
{
    private static readonly AnimalDefinition[] s_all =
    [
        new(AnimalKind.Chicken, "chicken", 800, ItemCatalog.Egg, ItemCatalog.LargeEgg, false, "White Chicken", 16, 16, 22),
        new(AnimalKind.Cow, "cow", 1_500, ItemCatalog.Milk, ItemCatalog.LargeMilk, true, "White Cow", 32, 32, 14),
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

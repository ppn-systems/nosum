// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Nosum.AtlasPacker;

/// <summary>A sprite to place: its name and pixel size.</summary>
/// <param name="Name">Sprite name, unique within one atlas.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
internal readonly record struct PackItem(string Name, int Width, int Height);

/// <summary>Where a sprite landed in the atlas.</summary>
/// <param name="Name">Sprite name.</param>
/// <param name="X">Left edge in pixels.</param>
/// <param name="Y">Top edge in pixels.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
internal readonly record struct Placement(string Name, int X, int Y, int Width, int Height);

/// <summary>A packed layout: atlas size and one placement per input item.</summary>
/// <param name="Width">Atlas width in pixels (a power of two).</param>
/// <param name="Height">Atlas height in pixels (a power of two).</param>
/// <param name="Placements">Placements, ordered by sprite name.</param>
internal sealed record PackResult(int Width, int Height, IReadOnlyList<Placement> Placements);

/// <summary>
/// Deterministic shelf packer: sprites sorted tallest first are laid left to right in rows
/// ("shelves") of a power-of-two-wide atlas. Same input, same layout — so a re-pack of
/// unchanged art produces an identical atlas.
/// </summary>
internal static class ShelfPacker
{
    /// <summary>Largest atlas side the packer will produce.</summary>
    public const int MaxSide = 4096;

    /// <summary>
    /// Packs the items.
    /// </summary>
    /// <param name="items">Sprites to place; names must be unique.</param>
    /// <param name="padding">Empty pixels kept between sprites and around the atlas edge.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ArgumentException">An item has a non-positive size or a duplicate name.</exception>
    /// <exception cref="InvalidOperationException">The items do not fit in a <see cref="MaxSide"/> square.</exception>
    public static PackResult Pack(IReadOnlyList<PackItem> items, int padding)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(padding);

        HashSet<string> names = new(StringComparer.Ordinal);
        long area = 0;
        int widest = 0;
        foreach (PackItem item in items)
        {
            if (item.Width <= 0 || item.Height <= 0)
            {
                throw new ArgumentException($"Sprite '{item.Name}' has an empty size.", nameof(items));
            }

            if (!names.Add(item.Name))
            {
                throw new ArgumentException($"Sprite name '{item.Name}' appears twice.", nameof(items));
            }

            area += (long)(item.Width + padding) * (item.Height + padding);
            widest = Math.Max(widest, item.Width + (2 * padding));
        }

        if (items.Count == 0)
        {
            return new PackResult(0, 0, []);
        }

        List<PackItem> order = [.. items];
        order.Sort(static (a, b) =>
        {
            int byHeight = b.Height.CompareTo(a.Height);
            if (byHeight != 0)
            {
                return byHeight;
            }

            int byWidth = b.Width.CompareTo(a.Width);
            return byWidth != 0 ? byWidth : string.CompareOrdinal(a.Name, b.Name);
        });

        int width = NextPowerOfTwo(Math.Max(widest, (int)Math.Ceiling(Math.Sqrt(area))));
        while (width <= MaxSide)
        {
            if (TryPlace(order, width, padding, out List<Placement> placements, out int usedHeight))
            {
                int height = NextPowerOfTwo(usedHeight);
                if (height <= MaxSide)
                {
                    placements.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
                    return new PackResult(width, height, placements);
                }
            }

            width *= 2;
        }

        throw new InvalidOperationException($"Sprites do not fit in a {MaxSide}x{MaxSide} atlas; split the category.");
    }

    private static bool TryPlace(List<PackItem> order, int width, int padding, out List<Placement> placements, out int usedHeight)
    {
        placements = new List<Placement>(order.Count);
        int x = padding;
        int y = padding;
        int shelfHeight = 0;

        foreach (PackItem item in order)
        {
            if (x + item.Width + padding > width)
            {
                x = padding;
                y += shelfHeight + padding;
                shelfHeight = 0;
            }

            if (x + item.Width + padding > width)
            {
                usedHeight = 0;
                return false;
            }

            placements.Add(new Placement(item.Name, x, y, item.Width, item.Height));
            x += item.Width + padding;
            shelfHeight = Math.Max(shelfHeight, item.Height);
        }

        usedHeight = y + shelfHeight + padding;
        return true;
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Nosum.AtlasPacker;
using Xunit;

namespace Nosum.UnitTests.Tools;

public sealed class ShelfPackerTests
{
    [Fact]
    public void Pack_Items_NoTwoPlacementsOverlapAndAllFitInsideTheAtlas()
    {
        List<PackItem> items = [];
        for (int i = 0; i < 40; i++)
        {
            items.Add(new PackItem($"s{i:D2}", 32, i % 3 == 0 ? 48 : 32));
        }

        PackResult result = ShelfPacker.Pack(items, padding: 2);

        result.Placements.Should().HaveCount(items.Count);
        foreach (Placement p in result.Placements)
        {
            p.X.Should().BeGreaterThanOrEqualTo(2);
            p.Y.Should().BeGreaterThanOrEqualTo(2);
            (p.X + p.Width).Should().BeLessThanOrEqualTo(result.Width - 2);
            (p.Y + p.Height).Should().BeLessThanOrEqualTo(result.Height - 2);
        }

        for (int a = 0; a < result.Placements.Count; a++)
        {
            for (int b = a + 1; b < result.Placements.Count; b++)
            {
                Overlaps(result.Placements[a], result.Placements[b]).Should().BeFalse();
            }
        }
    }

    [Fact]
    public void Pack_Items_AtlasSidesArePowersOfTwo()
    {
        PackResult result = ShelfPacker.Pack([new PackItem("a", 32, 32), new PackItem("b", 40, 20)], padding: 2);

        IsPowerOfTwo(result.Width).Should().BeTrue();
        IsPowerOfTwo(result.Height).Should().BeTrue();
    }

    [Fact]
    public void Pack_SameItemsInDifferentOrder_ProducesTheSameLayout()
    {
        PackItem[] items = [new("grass", 32, 32), new("farmer", 32, 48), new("soil", 32, 32)];
        PackItem[] reversed = [items[2], items[1], items[0]];

        PackResult first = ShelfPacker.Pack(items, padding: 2);
        PackResult second = ShelfPacker.Pack(reversed, padding: 2);

        second.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Pack_DuplicateName_Throws()
    {
        Action act = () => ShelfPacker.Pack([new PackItem("a", 32, 32), new PackItem("a", 16, 16)], padding: 2);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Pack_NoItems_ReturnsEmptyAtlas()
    {
        PackResult result = ShelfPacker.Pack([], padding: 2);

        result.Width.Should().Be(0);
        result.Placements.Should().BeEmpty();
    }

    private static bool Overlaps(Placement a, Placement b)
        => a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}

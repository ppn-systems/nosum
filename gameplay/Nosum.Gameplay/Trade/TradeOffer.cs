// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Trade;

/// <summary>One line of an offer: an item and how many of it.</summary>
/// <param name="ItemId">Item id.</param>
/// <param name="Quantity">How many.</param>
public readonly record struct TradeLine(int ItemId, int Quantity);

/// <summary>What one side puts into a trade: item lines and coins. Immutable; an empty offer has no lines and no coins.</summary>
public sealed class TradeOffer : IEquatable<TradeOffer>
{
    /// <summary>Most different items one side may offer.</summary>
    public const int MaxLines = 9;

    /// <summary>Gets the offer that holds nothing.</summary>
    public static readonly TradeOffer Empty = new([], 0);

    /// <summary>Initializes an offer; the lines are copied.</summary>
    /// <param name="lines">The item lines.</param>
    /// <param name="coins">The coins.</param>
    public TradeOffer(IEnumerable<TradeLine> lines, long coins)
    {
        ArgumentNullException.ThrowIfNull(lines);
        this.Lines = [.. lines];
        this.Coins = coins;
    }

    /// <summary>Gets the item lines.</summary>
    public IReadOnlyList<TradeLine> Lines { get; }

    /// <summary>Gets the offered coins.</summary>
    public long Coins { get; }

    /// <summary>Gets whether nothing is offered.</summary>
    public bool IsEmpty => this.Lines.Count == 0 && this.Coins == 0;

    /// <summary>Checks the shape of an offer: lines are known tradable items with positive quantities, no item twice, coins in range.</summary>
    /// <returns>The reason it is invalid, or <see cref="TradeVerdict.Ok"/>.</returns>
    public TradeVerdict Validate()
    {
        if (this.Lines.Count > MaxLines || this.Coins < 0 || this.Coins > CoinRules.MaxBalance)
        {
            return TradeVerdict.InvalidOffer;
        }

        HashSet<int> seen = [];
        foreach (TradeLine line in this.Lines)
        {
            if (!ItemCatalog.TryGet(line.ItemId, out ItemDefinition? item) || !item.IsTradable || line.Quantity < 1 || line.Quantity > item.MaxStack * 36 || !seen.Add(line.ItemId))
            {
                return TradeVerdict.InvalidOffer;
            }
        }

        return TradeVerdict.Ok;
    }

    /// <inheritdoc/>
    public bool Equals(TradeOffer? other)
    {
        if (other is null || this.Coins != other.Coins || this.Lines.Count != other.Lines.Count)
        {
            return false;
        }

        // Order does not matter: the same lines in another order are the same offer.
        foreach (TradeLine line in this.Lines)
        {
            bool found = false;
            foreach (TradeLine candidate in other.Lines)
            {
                if (candidate == line)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => this.Equals(obj as TradeOffer);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Coins, this.Lines.Count);
}

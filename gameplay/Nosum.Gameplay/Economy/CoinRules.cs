// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Economy;

/// <summary>
/// Coin amounts and the market's numbers. The values are product defaults chosen by the implementing agent, not balance
/// (docs/adr/ADR-013); they are constants here so a decision changes one line.
/// </summary>
public static class CoinRules
{
    /// <summary>Coins a new player starts with.</summary>
    public const long StartingCoins = 500;

    /// <summary>Largest balance a player may hold; a credit past it is refused so a sum can never overflow.</summary>
    public const long MaxBalance = 1_000_000_000_000;

    /// <summary>Market fee, percent of the price, taken from the seller's proceeds (rounded down).</summary>
    public const int MarketFeePercent = 5;

    /// <summary>Smallest listing price (for the whole stack).</summary>
    public const long MinListingPrice = 1;

    /// <summary>Largest listing price (for the whole stack).</summary>
    public const long MaxListingPrice = 1_000_000_000;

    /// <summary>Hours a listing stays on the market before it expires and the items go back to the seller.</summary>
    public const int ListingHours = 48;

    /// <summary>Most active listings one player may have.</summary>
    public const int MaxActiveListings = 20;

    /// <summary>Most units of one item id one player may have listed at once, over all their listings (docs/adr/ADR-030 D4: no cornering a single item).</summary>
    public const int MaxListedUnitsPerItem = 500;

    /// <summary>Listings shown per page when browsing.</summary>
    public const int ListingsPerPage = 10;

    /// <summary>Calculates the market fee of a sale.</summary>
    /// <param name="price">The listing price.</param>
    /// <returns>The fee, rounded up: never less than the exact share, so a sale split into micro-listings pays no less; never more than the price.</returns>
    public static long MarketFee(long price)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(price);
        return (price * MarketFeePercent + 99) / 100;
    }

    /// <summary>Calculates what the seller receives.</summary>
    /// <param name="price">The listing price.</param>
    /// <returns>The price minus the fee.</returns>
    public static long SellerProceeds(long price) => price - MarketFee(price);

    /// <summary>Adds <paramref name="delta"/> to <paramref name="current"/>, clamped between 0 and <see cref="MaxBalance"/>.</summary>
    /// <param name="current">Current coin balance.</param>
    /// <param name="delta">Signed change in coins.</param>
    /// <returns>Clamped coin balance.</returns>
    public static long ClampedAdd(long current, long delta)
    {
        if (delta >= 0)
        {
            long headroom = MaxBalance - Math.Clamp(current, 0, MaxBalance);
            return Math.Clamp(current, 0, MaxBalance) + Math.Min(delta, headroom);
        }

        long next = current + delta;
        return Math.Clamp(next, 0, MaxBalance);
    }

    /// <summary>Adds <paramref name="delta"/> to <paramref name="current"/>, clamped to a 32-bit unsigned wire coin count.</summary>
    /// <param name="current">Current wire coin balance.</param>
    /// <param name="delta">Signed change in coins.</param>
    /// <returns>Clamped <see cref="uint"/> coin balance.</returns>
    public static uint ClampedAdd(uint current, long delta)
    {
        long next = ClampedAdd((long)current, delta);
        return (uint)Math.Clamp(next, 0L, uint.MaxValue);
    }
}

/// <summary>
/// The fixed price the shipping bin pays, per item: the <see cref="ItemDefinition.SellPrice"/> of every produce and forage item
/// (docs/adr/ADR-025). Seeds, tools and materials are not bought. The prices are balance data in <see cref="ItemCatalog"/>.
/// </summary>
public static class ShippingPrices
{
    /// <summary>Looks up what the bin pays for one item.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="coinsEach">Coins for one item.</param>
    /// <returns><see langword="true"/> when the bin buys this item.</returns>
    public static bool TryGet(int itemId, out int coinsEach)
    {
        coinsEach = ItemCatalog.TryGet(itemId, out ItemDefinition? item) && item.Kind == ItemKind.Produce ? item.SellPrice : 0;
        return coinsEach > 0;
    }

    /// <summary>Looks up the sell value of any item with a positive <see cref="ItemDefinition.SellPrice"/> (including relics, trophies and gear).</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="coinsEach">Coins for one item.</param>
    /// <returns><see langword="true"/> when the item has a positive sell price.</returns>
    public static bool TryGetSellPrice(int itemId, out int coinsEach)
    {
        coinsEach = ItemCatalog.TryGet(itemId, out ItemDefinition? item) && item.SellPrice > 0 ? item.SellPrice : 0;
        return coinsEach > 0;
    }
}

/// <summary>Why a listing was refused.</summary>
public enum MarketVerdict
{
    /// <summary>The listing is allowed.</summary>
    Ok = 0,

    /// <summary>The item id is not in the catalogue.</summary>
    UnknownItem = 1,

    /// <summary>The item cannot be sold.</summary>
    NotTradable = 2,

    /// <summary>The quantity is zero, negative or larger than a stack.</summary>
    InvalidQuantity = 3,

    /// <summary>The price is outside the allowed range.</summary>
    InvalidPrice = 4,

    /// <summary>The seller already has the most listings allowed.</summary>
    TooManyListings = 5,

    /// <summary>The seller would have more than <see cref="CoinRules.MaxListedUnitsPerItem"/> units of this item listed at once.</summary>
    TooManyOfItem = 6,
}

/// <summary>Rules for putting a stack on the consignment market.</summary>
public static class MarketRules
{
    /// <summary>
    /// The id of the market counter, a <c>ShopPoint</c> of the Town (docs/adr/ADR-034). It is not a shop id (shops are 1 to 99) and differs from the community board's counter (1000), so
    /// <c>ShopRules.TryFindShop</c> can return it like any other counter.
    /// </summary>
    public const int MarketCounterId = 1001;

    /// <summary>Checks a new listing.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">How many.</param>
    /// <param name="price">The price for the whole stack.</param>
    /// <param name="activeListings">How many active listings the seller already has.</param>
    /// <param name="listedUnitsOfItem">How many units of this item the seller already has listed, over all their active listings.</param>
    /// <returns><see cref="MarketVerdict.Ok"/> or the reason.</returns>
    public static MarketVerdict ValidateListing(int itemId, int quantity, long price, int activeListings, int listedUnitsOfItem = 0)
    {
        if (!ItemCatalog.TryGet(itemId, out ItemDefinition? item))
        {
            return MarketVerdict.UnknownItem;
        }

        if (!item.IsTradable)
        {
            return MarketVerdict.NotTradable;
        }

        if (quantity < 1 || quantity > item.MaxStack)
        {
            return MarketVerdict.InvalidQuantity;
        }

        if (price < CoinRules.MinListingPrice || price > CoinRules.MaxListingPrice)
        {
            return MarketVerdict.InvalidPrice;
        }

        if (activeListings >= CoinRules.MaxActiveListings)
        {
            return MarketVerdict.TooManyListings;
        }

        return (long)listedUnitsOfItem + quantity > CoinRules.MaxListedUnitsPerItem ? MarketVerdict.TooManyOfItem : MarketVerdict.Ok;
    }
}

/// <summary>Where the shipping bin accepts goods from.</summary>
public static class ShippingBinRules
{
    /// <summary>How far from the bin's footprint a farmer's feet may be, world pixels (a little over two tiles).</summary>
    public const double ReachPixels = 40;

    /// <summary>Tells whether feet are close enough to the farm's shipping bin.</summary>
    /// <param name="feetX">Feet x, world pixels.</param>
    /// <param name="feetY">Feet y, world pixels.</param>
    /// <param name="tileSize">Tile size, pixels.</param>
    /// <returns><see langword="true"/> when the farmer can reach the bin.</returns>
    public static bool InReach(double feetX, double feetY, int tileSize)
    {
        foreach (Nosum.Core.PropPlacement placement in Nosum.Gameplay.Farm.FarmProps.Placements)
        {
            if (!ReferenceEquals(placement.Definition, Nosum.Gameplay.Farm.FarmProps.ShippingBin))
            {
                continue;
            }

            double left = placement.TileX * tileSize;
            double top = placement.TileY * tileSize;
            double right = left + (placement.Definition.FootprintWidth * tileSize);
            double bottom = top + (placement.Definition.FootprintHeight * tileSize);
            double dx = Math.Max(Math.Max(left - feetX, 0), feetX - right);
            double dy = Math.Max(Math.Max(top - feetY, 0), feetY - bottom);
            return (dx * dx) + (dy * dy) <= ReachPixels * ReachPixels;
        }

        return false;
    }
}

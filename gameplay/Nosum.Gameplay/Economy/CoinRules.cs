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

    /// <summary>Listings shown per page when browsing.</summary>
    public const int ListingsPerPage = 10;

    /// <summary>Calculates the market fee of a sale.</summary>
    /// <param name="price">The listing price.</param>
    /// <returns>The fee, rounded down; never more than the price.</returns>
    public static long MarketFee(long price)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(price);
        return price / 100 * MarketFeePercent + (price % 100 * MarketFeePercent / 100);
    }

    /// <summary>Calculates what the seller receives.</summary>
    /// <param name="price">The listing price.</param>
    /// <returns>The price minus the fee.</returns>
    public static long SellerProceeds(long price) => price - MarketFee(price);
}

/// <summary>The fixed price the shipping bin pays for crops, per item. Only produce is bought; the table is a development default.</summary>
public static class ShippingPrices
{
    /// <summary>Looks up what the bin pays for one item.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="coinsEach">Coins for one item.</param>
    /// <returns><see langword="true"/> when the bin buys this item.</returns>
    public static bool TryGet(int itemId, out int coinsEach)
    {
        coinsEach = itemId switch
        {
            ItemCatalog.Parsnip => 20,
            ItemCatalog.Radish => 30,
            ItemCatalog.Cauliflower => 60,
            _ => 0,
        };
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
}

/// <summary>Rules for putting a stack on the consignment market.</summary>
public static class MarketRules
{
    /// <summary>Checks a new listing.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">How many.</param>
    /// <param name="price">The price for the whole stack.</param>
    /// <param name="activeListings">How many active listings the seller already has.</param>
    /// <returns><see cref="MarketVerdict.Ok"/> or the reason.</returns>
    public static MarketVerdict ValidateListing(int itemId, int quantity, long price, int activeListings)
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

        return activeListings >= CoinRules.MaxActiveListings ? MarketVerdict.TooManyListings : MarketVerdict.Ok;
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

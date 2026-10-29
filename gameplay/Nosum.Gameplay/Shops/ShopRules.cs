// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;

namespace Nosum.Gameplay.Shops;

/// <summary>Why a purchase was refused before any coin moved.</summary>
public enum ShopVerdict
{
    /// <summary>The purchase is allowed.</summary>
    Ok = 0,

    /// <summary>The shop id is not in the catalogue.</summary>
    UnknownShop = 1,

    /// <summary>The shop does not sell this item.</summary>
    NotSold = 2,

    /// <summary>The quantity is zero, negative or larger than a stack.</summary>
    InvalidQuantity = 3,
}

/// <summary>Rules for buying at a shop and for standing at one. Pure; the server decides with them and the client predicts with them.</summary>
public static class ShopRules
{
    /// <summary>How far from the shop point's tile a farmer's feet may be, world pixels (a little over two tiles, like the shipping bin).</summary>
    public const double ReachPixels = 40;

    /// <summary>Checks a purchase against the catalogue and prices it.</summary>
    /// <param name="shopId">The shop.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">How many.</param>
    /// <param name="total">The price of the whole purchase when allowed.</param>
    /// <returns><see cref="ShopVerdict.Ok"/> or the reason.</returns>
    public static ShopVerdict Price(int shopId, int itemId, int quantity, out long total)
    {
        total = 0;
        if (!ShopCatalog.TryGet(shopId, out ShopDefinition? shop))
        {
            return ShopVerdict.UnknownShop;
        }

        if (!shop.TryGetPrice(itemId, out int each) || !ItemCatalog.TryGet(itemId, out ItemDefinition? item))
        {
            return ShopVerdict.NotSold;
        }

        if (quantity < 1 || quantity > item.MaxStack)
        {
            return ShopVerdict.InvalidQuantity;
        }

        total = (long)each * quantity;
        return ShopVerdict.Ok;
    }

    /// <summary>
    /// Tells whether an item can be sold now: a seed only while its crop grows in the current season, a seasonal piece of furniture only in its
    /// season (<see cref="PlaceableDefinition.SoldIn"/>), anything else always.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <returns><see langword="false"/> for a seed that cannot be planted, or a seasonal piece outside its season, in the current season.</returns>
    public static bool IsInSeason(int itemId, GameCalendar calendar, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (PlaceableCatalog.TryGet(itemId, out PlaceableDefinition? piece) && piece.SoldIn is { } season)
        {
            return calendar.SeasonAt(nowMs) == season;
        }

        return !ItemCatalog.TryGet(itemId, out ItemDefinition? item)
            || item.Kind != ItemKind.Seed
            || !CropCatalog.TryGet(item.CropId, out CropDefinition? crop)
            || crop.GrowsIn(calendar.SeasonAt(nowMs));
    }

    /// <summary>Tells whether feet are close enough to a shop point.</summary>
    /// <param name="point">The shop point on its map.</param>
    /// <param name="feetX">Feet x, world pixels.</param>
    /// <param name="feetY">Feet y, world pixels.</param>
    /// <param name="tileSize">Tile size, pixels.</param>
    /// <returns><see langword="true"/> when the farmer can reach the shop.</returns>
    public static bool InReach(ShopPoint point, double feetX, double feetY, int tileSize)
    {
        double left = point.TileX * tileSize;
        double top = point.TileY * tileSize;
        double dx = Math.Max(Math.Max(left - feetX, 0), feetX - (left + tileSize));
        double dy = Math.Max(Math.Max(top - feetY, 0), feetY - (top + tileSize));
        return (dx * dx) + (dy * dy) <= ReachPixels * ReachPixels;
    }

    /// <summary>Tells whether feet are at the counter with a given id on a map: some point of that id is in reach and no other counter is nearer.</summary>
    /// <param name="map">The map the farmer is on.</param>
    /// <param name="counterId">The shop id, or <c>CommunityGoalCatalog.BoardCounterId</c>.</param>
    /// <param name="feetX">Feet x, world pixels.</param>
    /// <param name="feetY">Feet y, world pixels.</param>
    /// <param name="tileSize">Tile size, pixels.</param>
    /// <returns><see langword="true"/> when the action key at these feet would use that counter.</returns>
    public static bool IsAtCounter(MapSpec map, int counterId, double feetX, double feetY, int tileSize)
        => TryFindShop(map, feetX, feetY, tileSize, out int found) && found == counterId;

    /// <summary>
    /// Finds the counter a farmer on a map can use from where they stand: the nearest one in reach, so two counters close together (the
    /// community board next to the seed shop) each win on their own side.
    /// </summary>
    /// <param name="map">The map the farmer is on.</param>
    /// <param name="feetX">Feet x, world pixels.</param>
    /// <param name="feetY">Feet y, world pixels.</param>
    /// <param name="tileSize">Tile size, pixels.</param>
    /// <param name="shopId">The nearest counter's id when found (a shop id, or <c>CommunityGoalCatalog.BoardCounterId</c>).</param>
    /// <returns><see langword="true"/> when a counter of the map is in reach.</returns>
    public static bool TryFindShop(MapSpec map, double feetX, double feetY, int tileSize, out int shopId)
    {
        ArgumentNullException.ThrowIfNull(map);

        shopId = 0;
        double best = double.MaxValue;
        foreach (ShopPoint point in map.Shops)
        {
            if (!InReach(point, feetX, feetY, tileSize))
            {
                continue;
            }

            double left = point.TileX * tileSize;
            double top = point.TileY * tileSize;
            double dx = Math.Max(Math.Max(left - feetX, 0), feetX - (left + tileSize));
            double dy = Math.Max(Math.Max(top - feetY, 0), feetY - (top + tileSize));
            double distance = (dx * dx) + (dy * dy);
            if (distance < best)
            {
                best = distance;
                shopId = point.ShopId;
            }
        }

        return shopId != 0;
    }
}

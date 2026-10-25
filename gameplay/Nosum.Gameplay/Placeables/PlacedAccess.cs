// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Maps;

namespace Nosum.Gameplay.Placeables;

/// <summary>Who may use the objects standing in an instance: open a chest, feed a furnace (docs/adr/ADR-024 D4).</summary>
public static class PlacedAccess
{
    /// <summary>Tells whether a player may use the chests and furnaces of an instance.</summary>
    /// <param name="instance">The place the object stands in.</param>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="memberOfInstanceCoop">Whether the player belongs to the co-op that owns the instance; only asked for co-op land.</param>
    /// <returns>
    /// <see langword="true"/> for the owner of a private farm or farmhouse, and for the members of the co-op on its land. A visitor, and anybody in a
    /// shared place, may not.
    /// </returns>
    public static bool MayUse(MapInstance instance, Guid playerId, bool memberOfInstanceCoop)
    {
        if (!instance.IsPrivate)
        {
            return false;
        }

        return instance.IsCoopLand ? memberOfInstanceCoop : instance.OwnerId == playerId;
    }

    /// <summary>Tells whether any tile of a piece's footprint is within the farming reach of a player's feet.</summary>
    /// <param name="view">The piece's view in its rotation.</param>
    /// <param name="tileX">Left tile of the footprint.</param>
    /// <param name="tileY">Top tile of the footprint.</param>
    /// <param name="feetX">Feet x, world pixels.</param>
    /// <param name="feetY">Feet y, world pixels.</param>
    /// <param name="tileSize">Tile size, pixels.</param>
    /// <returns><see langword="true"/> when the piece can be worked from there.</returns>
    public static bool Reaches(PlaceableView view, int tileX, int tileY, double feetX, double feetY, int tileSize)
    {
        for (int y = tileY; y < tileY + view.FootprintHeight; y++)
        {
            for (int x = tileX; x < tileX + view.FootprintWidth; x++)
            {
                if (Farm.FarmRules.InReach(feetX, feetY, x, y, tileSize))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Shops;

namespace Nosum.Infrastructure.Shops;

/// <summary>
/// Where a player stands, for the counters that serve one kind of request (the blacksmith, the tailor): the player is at a counter when a point of that
/// counter is in reach of their feet on their map. Reads the server's own position record, never a value from a packet.
/// </summary>
internal static class CounterReach
{
    /// <summary>Tells whether a player's feet are in reach of a point of the given counter on the player's map.</summary>
    /// <param name="maps">The hosted maps (for the tile size).</param>
    /// <param name="player">The player's presence record.</param>
    /// <param name="counterId">The counter's shop id, for example <c>ShopCatalog.Blacksmith</c>.</param>
    /// <returns><see langword="true"/> when the player may use that counter.</returns>
    internal static bool IsAtCounter(IMapCatalog maps, PlayerPresence player, int counterId)
    {
        if (!GameMaps.TryGet(player.MapId, out MapSpec? spec) || !maps.TryGet(player.MapId, out MapDefinition? map))
        {
            return false;
        }

        double feetX;
        double feetY;
        lock (player.Gate)
        {
            feetX = player.Motion.X;
            feetY = player.Motion.Y;
        }

        foreach (ShopPoint point in spec.Shops)
        {
            if (point.ShopId == counterId && ShopRules.InReach(point, feetX, feetY, map.Grid.TileSize))
            {
                return true;
            }
        }

        return false;
    }
}

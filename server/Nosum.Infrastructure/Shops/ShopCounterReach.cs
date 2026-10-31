// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Shops;
using Nalix.Abstractions.Networking;

namespace Nosum.Infrastructure.Shops;

/// <summary>
/// Whether a player's feet stand within reach of one of a shop's counters (docs/adr/ADR-013, ADR-075 amendment). The position is the server's own
/// record of the player, never a value from a packet. Shared by the shop counter and the pet counter (the carpenter's).
/// </summary>
public static class ShopCounterReach
{
    /// <summary>Tells whether the connection's player stands within reach of a counter of the shop on the player's map.</summary>
    /// <param name="presence">Where players stand.</param>
    /// <param name="maps">The hosted maps (tile size).</param>
    /// <param name="connection">The player's connection.</param>
    /// <param name="shopId">The shop.</param>
    /// <returns><see langword="true"/> when the player is in reach of one of its counters on their map.</returns>
    public static bool IsAt(IMapPresence presence, IMapCatalog maps, IConnection connection, int shopId)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(connection);

        if (!presence.TryGet(connection, out PlayerPresence? player)
            || !GameMaps.TryGet(player.MapId, out MapSpec? spec)
            || !maps.TryGet(player.MapId, out MapDefinition? map))
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
            if (point.ShopId == shopId && ShopRules.InReach(point, feetX, feetY, map.Grid.TileSize))
            {
                return true;
            }
        }

        return false;
    }
}

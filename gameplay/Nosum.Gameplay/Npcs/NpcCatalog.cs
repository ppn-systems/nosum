// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Shops;

namespace Nosum.Gameplay.Npcs;

/// <summary>
/// The merchants of the world (docs/adr/ADR-043 D1): one per counter of the Town, standing on the counter's row a tile or two to the side of the counter's tile. The counters are the
/// <see cref="ShopPoint"/>s of <see cref="GameMaps"/>; the tile of a merchant is read from them, so moving a counter moves its merchant.
/// </summary>
public static class NpcCatalog
{
    private static readonly NpcDefinition[] s_all = Build();
    private static readonly Dictionary<string, NpcDefinition[]> s_byMap = ByMap(s_all);

    /// <summary>Gets every merchant.</summary>
    public static IReadOnlyList<NpcDefinition> All => s_all;

    /// <summary>Finds a merchant by id.</summary>
    /// <param name="id">The merchant's id.</param>
    /// <param name="npc">The merchant when found.</param>
    /// <returns><see langword="true"/> when the id is in the catalogue.</returns>
    public static bool TryGet(string? id, [NotNullWhen(true)] out NpcDefinition? npc)
    {
        foreach (NpcDefinition candidate in s_all)
        {
            if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
            {
                npc = candidate;
                return true;
            }
        }

        npc = null;
        return false;
    }

    /// <summary>Finds the merchant who serves a counter.</summary>
    /// <param name="counterId">The counter: a shop id, <c>CommunityGoalCatalog.BoardCounterId</c> or <c>MarketRules.MarketCounterId</c>.</param>
    /// <param name="npc">The merchant when found.</param>
    /// <returns><see langword="true"/> when a merchant serves that counter.</returns>
    public static bool TryGetByCounter(int counterId, [NotNullWhen(true)] out NpcDefinition? npc)
    {
        foreach (NpcDefinition candidate in s_all)
        {
            if (candidate.CounterId == counterId)
            {
                npc = candidate;
                return true;
            }
        }

        npc = null;
        return false;
    }

    /// <summary>Lists the merchants standing on a map. Allocation-free: the frame asks this every frame.</summary>
    /// <param name="mapId">The map.</param>
    /// <returns>The merchants of the map, possibly none.</returns>
    public static IReadOnlyList<NpcDefinition> OnMap(string mapId) => mapId is not null && s_byMap.TryGetValue(mapId, out NpcDefinition[]? found) ? found : [];

    private static Dictionary<string, NpcDefinition[]> ByMap(NpcDefinition[] all)
    {
        Dictionary<string, List<NpcDefinition>> grouped = new(StringComparer.Ordinal);
        foreach (NpcDefinition npc in all)
        {
            if (!grouped.TryGetValue(npc.MapId, out List<NpcDefinition>? list))
            {
                list = [];
                grouped[npc.MapId] = list;
            }

            list.Add(npc);
        }

        Dictionary<string, NpcDefinition[]> result = new(StringComparer.Ordinal);
        foreach ((string map, List<NpcDefinition> list) in grouped)
        {
            result[map] = [.. list];
        }

        return result;
    }

    private static NpcDefinition[] Build() =>
    [
        // Each merchant stands on the doormat row, beside the counter's tile, on a tile that is walkable ground with no awning over it: the door tile behind the counter has
        // the shop's awning on a front layer, which would draw over the merchant's chest. The farmer stands on the counter tile, so the two never overlap.
        Place("seed_keeper", ShopCatalog.SeedShop, 2, "seed_keeper", NpcActions.Buy | NpcActions.Talk),
        Place("tailor", ShopCatalog.ClothingShop, 1, "tailor", NpcActions.Buy | NpcActions.Talk),
        Place("furnisher", ShopCatalog.FurnitureShop, -1, "furnisher", NpcActions.Buy | NpcActions.Talk),
        Place("blacksmith", ShopCatalog.Blacksmith, 1, "blacksmith", NpcActions.Buy | NpcActions.Upgrade | NpcActions.Talk),
        Place("carpenter", ShopCatalog.CarpenterShop, -1, "carpenter", NpcActions.Buy | NpcActions.Build | NpcActions.Talk),
        Place("trader", MarketRules.MarketCounterId, 1, "trader", NpcActions.Buy | NpcActions.Sell | NpcActions.Talk),
        Place("board_keeper", CommunityGoalCatalog.BoardCounterId, -1, "board_keeper", NpcActions.Goals | NpcActions.Talk),
    ];

    private static NpcDefinition Place(string id, int counterId, int dx, string art, NpcActions actions)
    {
        if (!GameMaps.TryGet(GameMaps.Town, out MapSpec? town))
        {
            throw new InvalidOperationException("The Town is not in the map catalogue.");
        }

        foreach (ShopPoint counter in town.Shops)
        {
            if (counter.ShopId == counterId)
            {
                return new NpcDefinition(id, town.Id, counterId, art, art + "_portrait", counter.TileX + dx, counter.TileY, actions);
            }
        }

        throw new InvalidOperationException("The Town has no counter " + counterId + " for the merchant " + id + ".");
    }
}

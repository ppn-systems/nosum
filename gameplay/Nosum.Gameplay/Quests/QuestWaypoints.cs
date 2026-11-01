// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Npcs;

namespace Nosum.Gameplay.Quests;

/// <summary>A tile on a map that a quest sends the player to.</summary>
/// <param name="MapId">The map.</param>
/// <param name="TileX">Tile column.</param>
/// <param name="TileY">Tile row.</param>
public readonly record struct QuestWaypoint(string MapId, int TileX, int TileY);

/// <summary>
/// Where the quests send the player (docs/adr/ADR-093 D1). Data only, by key: each quest names a place by its key, and a place is a merchant (its tile is read from
/// <see cref="NpcCatalog"/>, so moving a counter moves its waypoint) or a map (its spawn tile, for the places a quest sends the player into). The client draws the
/// waypoint as an arrow at the screen edge and a dot on the minimap; the server does not read this table.
/// </summary>
public static class QuestWaypoints
{
    // Quest key to place key. A quest with no fixed place (tilling, watering, cooking) has no entry and shows no waypoint.
    private static readonly (string Quest, string Place)[] s_questPlaces =
    [
        ("buy_seeds", "seed_keeper"),
        ("upgrade_tool", "blacksmith"),
        ("build_coop", "carpenter"),
        ("buy_animal", "carpenter"),
        ("decorate", "furnisher"),
        ("fill_request", "board_keeper"),
        ("chop_tree", GameMaps.Forest),
        ("mine_ore", GameMaps.Mine),
    ];

    private static readonly Dictionary<string, QuestWaypoint> s_places = BuildPlaces();
    private static readonly Dictionary<string, string> s_placeByQuest = BuildQuestPlaces();

    /// <summary>Looks up the waypoint a quest sends the player to.</summary>
    /// <param name="questKey">The quest's key (<see cref="QuestDefinition.Key"/>).</param>
    /// <param name="waypoint">The waypoint when the quest has one.</param>
    /// <returns><see langword="true"/> when the quest has a waypoint.</returns>
    public static bool TryGetForQuest(string questKey, [NotNullWhen(true)] out QuestWaypoint? waypoint)
    {
        if (questKey is not null && s_placeByQuest.TryGetValue(questKey, out string? place) && s_places.TryGetValue(place, out QuestWaypoint found))
        {
            waypoint = found;
            return true;
        }

        waypoint = null;
        return false;
    }

    /// <summary>Gets the place a quest sends the player to.</summary>
    /// <param name="questKey">The quest's key.</param>
    /// <returns>The place key, or <see langword="null"/> when the quest has no waypoint.</returns>
    public static string? PlaceOf(string questKey) => questKey is not null && s_placeByQuest.TryGetValue(questKey, out string? place) ? place : null;

    /// <summary>Looks up a place by its key.</summary>
    /// <param name="placeKey">The place's key: a merchant id (<c>seed_keeper</c>) or a map id (<c>mine</c>).</param>
    /// <param name="waypoint">The waypoint when the key names a place.</param>
    /// <returns><see langword="true"/> when the key names a place.</returns>
    public static bool TryGetPlace(string placeKey, [NotNullWhen(true)] out QuestWaypoint? waypoint)
    {
        if (placeKey is not null && s_places.TryGetValue(placeKey, out QuestWaypoint found))
        {
            waypoint = found;
            return true;
        }

        waypoint = null;
        return false;
    }

    /// <summary>Gets every quest key that has a waypoint, and the place key it names.</summary>
    /// <returns>The pairs, in table order.</returns>
    public static IEnumerable<(string Quest, string Place)> Pairs()
    {
        foreach ((string quest, string place) in s_questPlaces)
        {
            yield return (quest, place);
        }
    }

    private static Dictionary<string, QuestWaypoint> BuildPlaces()
    {
        Dictionary<string, QuestWaypoint> places = new(StringComparer.Ordinal);
        foreach (NpcDefinition npc in NpcCatalog.All)
        {
            places[npc.Id] = new QuestWaypoint(npc.MapId, npc.TileX, npc.TileY);
        }

        // The Forest's place is the tree nearest its arrival (Forest.tmx, Paths layer, tile 11), and the Mine's is the cave shaft down to the ore
        // (MineWorld.CaveShaft): the spawn tile is not where a player chops or mines (ADR-095 D1). Ore lies on the floors below, which are generated per player.
        foreach ((string placeKey, string mapId, (int X, int Y) node) in new[]
                 {
                     (GameMaps.Forest, GameMaps.Forest, (65, 9)),
                     (GameMaps.Mine, GameMaps.Mine, MineWorld.CaveShaft),
                 })
        {
            if (!GameMaps.TryGet(mapId, out MapSpec? map))
            {
                throw new InvalidOperationException("The quest place map " + mapId + " is not in the map catalogue.");
            }

            places[placeKey] = new QuestWaypoint(map.Id, node.X, node.Y);
        }

        return places;
    }

    private static Dictionary<string, string> BuildQuestPlaces()
    {
        Dictionary<string, string> byQuest = new(StringComparer.Ordinal);
        foreach ((string quest, string place) in s_questPlaces)
        {
            byQuest[quest] = place;
        }

        return byQuest;
    }
}

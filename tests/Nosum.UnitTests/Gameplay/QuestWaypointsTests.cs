// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Quests;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class QuestWaypointsTests
{
    [Fact]
    public void TryGetForQuest_BuySeeds_IsTheSeedKeepersTileInTheTown()
    {
        QuestWaypoints.TryGetForQuest("buy_seeds", out QuestWaypoint? waypoint).Should().BeTrue();

        waypoint!.Value.MapId.Should().Be(GameMaps.Town);
        QuestWaypoints.TryGetPlace("seed_keeper", out QuestWaypoint? merchant).Should().BeTrue();
        waypoint.Should().Be(merchant);
    }

    [Fact]
    public void TryGetForQuest_ChopTree_IsTheForestSpawnTile()
    {
        QuestWaypoints.TryGetForQuest("chop_tree", out QuestWaypoint? waypoint).Should().BeTrue();

        GameMaps.TryGet(GameMaps.Forest, out MapSpec? forest).Should().BeTrue();
        waypoint!.Value.Should().Be(new QuestWaypoint(GameMaps.Forest, forest!.SpawnTileX, forest.SpawnTileY));
    }

    [Fact]
    public void TryGetForQuest_TillSoil_HasNoWaypoint()
    {
        QuestWaypoints.TryGetForQuest("till_soil", out QuestWaypoint? waypoint).Should().BeFalse();
        waypoint.Should().BeNull();
    }

    [Fact]
    public void PlaceOf_UpgradeTool_IsTheBlacksmith()
    {
        QuestWaypoints.PlaceOf("upgrade_tool").Should().Be("blacksmith");
        QuestWaypoints.PlaceOf("send_emote").Should().BeNull();
    }

    [Fact]
    public void Pairs_EveryQuestAndPlaceNamedInTheTableExists()
    {
        foreach ((string quest, string place) in QuestWaypoints.Pairs())
        {
            QuestCatalog.All.Should().Contain(q => q.Key == quest, "the quest " + quest + " is in the catalogue");
            QuestWaypoints.TryGetPlace(place, out _).Should().BeTrue("the place " + place + " resolves");
        }
    }

    [Fact]
    public void Pairs_EveryWaypointIsOnTheMapItNames()
    {
        foreach ((string quest, _) in QuestWaypoints.Pairs())
        {
            QuestWaypoints.TryGetForQuest(quest, out QuestWaypoint? waypoint).Should().BeTrue();
            GameMaps.TryGet(waypoint!.Value.MapId, out _).Should().BeTrue("the map of " + quest + " is in the catalogue");
            waypoint.Value.TileX.Should().BeGreaterThanOrEqualTo(0);
            waypoint.Value.TileY.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void Pairs_NoQuestNamesAPlaceTwice()
    {
        QuestWaypoints.Pairs().Select(p => p.Quest).Should().OnlyHaveUniqueItems();
    }
}

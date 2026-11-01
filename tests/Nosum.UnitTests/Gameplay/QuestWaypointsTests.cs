// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
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
    public void TryGetForQuest_ChopTree_IsTheTreeNearTheForestArrival()
    {
        QuestWaypoints.TryGetForQuest("chop_tree", out QuestWaypoint? waypoint).Should().BeTrue();

        // Forest.tmx, Paths layer: the tree at (65, 9) is the one nearest the spawn tile (68, 0), where a player arrives and chops first.
        waypoint!.Value.Should().Be(new QuestWaypoint(GameMaps.Forest, 65, 9));
        GameMaps.TryGet(GameMaps.Forest, out MapSpec? forest).Should().BeTrue();
        waypoint.Value.Should().NotBe(new QuestWaypoint(GameMaps.Forest, forest!.SpawnTileX, forest.SpawnTileY), "the waypoint is the node, not the arrival");
    }

    [Fact]
    public void TryGetForQuest_MineOre_IsTheCaveShaftDownToTheOre()
    {
        QuestWaypoints.TryGetForQuest("mine_ore", out QuestWaypoint? waypoint).Should().BeTrue();

        // The ore lies on the floors below the cave, generated per player, so the fixed node to walk to is the shaft (MineWorld.CaveShaft).
        waypoint!.Value.Should().Be(new QuestWaypoint(GameMaps.Mine, MineWorld.CaveShaft.X, MineWorld.CaveShaft.Y));
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

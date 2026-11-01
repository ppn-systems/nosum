// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Contracts.Quest;
using Nosum.Contracts.Quest.S2C;
using Nosum.Gameplay.Quests;
using Nosum.Wasm.Socket.Quest;
using Nosum.Wasm.ViewModels.Quest;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class QuestWaypointViewModelTests
{
    private static QuestLog Log(params (int Id, QuestState State)[] quests)
    {
        QuestLog log = new();
        foreach ((int id, QuestState state) in quests)
        {
            log.Quests.Add(new QuestRow { QuestId = id, State = state, Progress = [0] });
        }

        return log;
    }

    [Fact]
    public void Waypoint_ActiveQuestWithAPlace_IsThatPlace()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Log((QuestCatalog.BuySeeds, QuestState.Active)));
        using QuestChannel channel = new(socket);
        using QuestWaypointViewModel vm = new(channel);

        channel.Load();

        vm.QuestKey.Should().Be("buy_seeds");
        QuestWaypoints.TryGetPlace("seed_keeper", out QuestWaypoint? seedKeeper).Should().BeTrue();
        vm.Waypoint.Should().Be(seedKeeper);
    }

    [Fact]
    public void Waypoint_NoActiveQuest_IsEmpty()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Log((QuestCatalog.BuySeeds, QuestState.Completed)));
        using QuestChannel channel = new(socket);
        using QuestWaypointViewModel vm = new(channel);

        channel.Load();

        vm.QuestKey.Should().BeNull();
        vm.Waypoint.Should().BeNull();
    }

    [Fact]
    public void Waypoint_TheActiveQuestIsInTheChainOrder_FirstActiveWins()
    {
        // Till soil comes first in the chain and has no place, so no waypoint shows while it is the active step.
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Log((QuestCatalog.TillSoil, QuestState.Active), (QuestCatalog.BuySeeds, QuestState.Active)));
        using QuestChannel channel = new(socket);
        using QuestWaypointViewModel vm = new(channel);

        channel.Load();

        vm.QuestKey.Should().BeNull();
        vm.Waypoint.Should().BeNull();
    }

    [Fact]
    public void Changed_WhenTheActiveQuestMovesOn_RaisesChangedOnce()
    {
        FakeGameSocket socket = new();
        int calls = 0;
        socket.ReplyWith(_ => calls++ == 0 ? Log((QuestCatalog.BuySeeds, QuestState.Active)) : Log((QuestCatalog.BuySeeds, QuestState.Completed), (QuestCatalog.UpgradeTool, QuestState.Active)));
        using QuestChannel channel = new(socket);
        using QuestWaypointViewModel vm = new(channel);
        channel.Load();
        int raised = 0;
        vm.Changed += () => raised++;

        channel.Load();

        vm.QuestKey.Should().Be("upgrade_tool");
        raised.Should().Be(1);
    }
}

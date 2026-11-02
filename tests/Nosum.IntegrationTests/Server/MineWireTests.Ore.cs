// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Node.S2C;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Nodes;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The mine's ore over real Nalix WebSocket sessions (docs/adr/ADR-026 D3): a copper ore node of a floor is broken with the starter pickaxe in three
/// swings, the last one leaves the node gone for everyone on the floor, and the ore lands in the miner's bag.
/// </summary>
public sealed partial class MineWireTests
{
    [Fact]
    public async Task Ore_CopperOreTakesThreeSwings_TheNodeGoesAndTheMinerKeepsTheOre()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client miner = await host.ConnectAsync();
        (NodePlacement ore, MineFloorPlanner.Plan plan) = await StandNextToAsync(host, miner, floor: 1, n => n.Kind == NodeKind.CopperOre);
        plan.Nodes.Should().Contain(n => n.Kind == NodeKind.CopperOre);

        NodeSnapshot snapshot = await miner.GetNodesAsync(host.Timeout);
        NodeRow node = snapshot.Nodes.Single(n => n.X == ore.X && n.Y == ore.Y);
        node.Kind.Should().Be((byte)NodeKind.CopperOre);

        int pickaxe = await miner.SlotOfAsync(ItemCatalog.Pickaxe);
        List<FarmAction> actions = [];
        for (int swing = 0; swing < 5; swing++)
        {
            UseItemResult result = await miner.UseAsync(pickaxe, ore.X, ore.Y, host.Timeout);
            actions.Add(result.Action);
            if (result.Action == FarmAction.Broke)
            {
                break;
            }
        }

        actions.Should().Equal(FarmAction.Hit, FarmAction.Hit, FarmAction.Broke);
        NodeChanged gone = await miner.NodeChanges.WaitAsync(c => c.Removed.Contains(node.Id), host.Timeout);
        gone.MapId.Should().Be(snapshot.MapId);
        (await miner.Slots.WaitAsync(s => s.ItemId == ItemCatalog.CopperOre && s.Quantity > 0, host.Timeout)).Quantity.Should().BeInRange(1, 3);
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Mine;
using Nosum.Contracts.Mine.C2S;
using Nosum.Contracts.Mine.S2C;
using Nosum.Contracts.Node.C2S;
using Nosum.Contracts.Node.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.Shop;
using Nosum.Contracts.Shop.C2S;
using Nosum.Contracts.Shop.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Domain.Blacksmith;
using Nosum.Domain.Farm;
using Nosum.Domain.Mines;
using Nosum.Domain.Nodes;
using Nosum.Domain.World;
using Nosum.Gameplay.Blacksmith;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Shops;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Visits;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The blacksmith, the tiered tools and the mine (docs/adr/ADR-026) over real Nalix WebSocket sessions against the real host and a real PostgreSQL database:
/// an upgrade with coins and bars that the smith holds for two game days (the dev clock skips them) and that survives a restart, the wide use of an upgraded hoe
/// and can, the damage of a copper pickaxe, a stone that hides a ladder, the elevator that remembers the deepest floor, and a floor that is nobody else's.
/// Floors are built from the player, the game day and the floor, so each test computes the same plan the server does and stands the player beside a node of it.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed partial class MineWireTests : IClassFixture<PostgresFixture>
{
    internal static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(90);
    internal static readonly DateTimeOffset s_start = new(2026, 11, 2, 3, 0, 0, TimeSpan.Zero); // after the calendar epoch

    // The blacksmith's counter in Town: the doormat in front of the door, standing on its bottom edge like every feet position.
    internal static readonly (float X, float Y) s_counter = ((MineWorld.BlacksmithCounter.X + 0.5f) * 16, ((MineWorld.BlacksmithCounter.Y + 1) * 16) - 1);

    private readonly PostgresFixture _database;

    public MineWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Ladder_TheStoneHidesIt_BreakingItRevealsIt_AndUsingItTakesThePlayerDownAndRemembersTheDepth()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client miner = await host.ConnectAsync();
        (NodePlacement stone, MineFloorPlanner.Plan plan) = await StandNextToAsync(host, miner, floor: 1, n => n.Variant == NodeVariants.LadderStone);
        plan.Ladder.Should().Be((stone.X, stone.Y));

        // The client is never told which stone hides the ladder.
        NodeSnapshot snapshot = await miner.GetNodesAsync(host.Timeout);
        snapshot.MapId.Should().Be("mine1");

        // The client gets the nodes of the chunks it is interested in (ADR-022), so part of a floor can be out of view.
        snapshot.Nodes.Select(n => (n.X, n.Y, n.Kind)).Should().NotBeEmpty().And.BeSubsetOf(Planned(plan));
        snapshot.Nodes.Should().OnlyContain(n => n.Variant == 0 && n.Kind != (byte)NodeKind.Ladder);
        NodeRow hidden = snapshot.Nodes.Single(n => n.X == stone.X && n.Y == stone.Y);
        hidden.Kind.Should().Be((byte)NodeKind.Stone);

        // Facing a ladder that is not there yet does nothing; breaking the stone reveals it in the same place with the same id.
        int pickaxe = await miner.SlotOfAsync(ItemCatalog.Pickaxe);
        int hoe = await miner.SlotOfAsync(ItemCatalog.Hoe);
        (await miner.UseAsync(hoe, stone.X, stone.Y, host.Timeout)).Action.Should().NotBe(FarmAction.Descended);
        (await miner.UseAsync(pickaxe, stone.X, stone.Y, host.Timeout)).Action.Should().Be(FarmAction.Broke);
        NodeChanged revealed = await miner.NodeChanges.WaitAsync(c => c.Upserted.Any(n => n.Kind == (byte)NodeKind.Ladder), host.Timeout);
        revealed.Upserted.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = hidden.Id, X = hidden.X, Y = hidden.Y, Kind = (byte)NodeKind.Ladder });
        (await miner.Slots.WaitAsync(s => s.ItemId == ItemCatalog.Stone, host.Timeout)).Quantity.Should().BeGreaterThan(0, "the stone still gives stone");

        // Using the ladder (any item in hand) takes the player down to the next floor, and the deepest floor is written.
        UseItemResult descended = await miner.UseAsync(hoe, stone.X, stone.Y, host.Timeout);
        descended.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Descended });
        (await miner.MapChanges.WaitAsync(m => m.MapId == "mine2", host.Timeout)).MapId.Should().Be("mine2");
        (await miner.GetNodesAsync(host.Timeout)).MapId.Should().Be("mine2");
        MineState state = await miner.GetMineAsync(host.Timeout);
        state.Should().BeEquivalentTo(new { Status = MineStatus.Ok, Floor = 2, Deepest = 2 });
        await using NosumDbContext db = new(_database.Options);
        (await db.PlayerMines.SingleAsync(m => m.PlayerId == miner.AccountId)).DeepestFloor.Should().Be(2);
        (await db.WorldNodes.AnyAsync(n => n.MapId == "mine1" || n.MapId == "mine2")).Should().BeFalse("floors live in memory only");
        (await db.NodeInstances.AnyAsync(n => n.MapId == "mine1" || n.MapId == "mine2")).Should().BeFalse();
    }

    [Fact]
    public async Task Elevator_RemembersTheDeepestFloor_OffersEveryFifthFloorDownToIt_AndWorksOnlyAtAnElevator()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        string token;
        Guid playerId;
        await using (Client diver = await host.ConnectAsync())
        {
            token = diver.SessionToken;
            playerId = diver.AccountId;
            await diver.PutOnAsync("mine5", -1, -1);
            (await diver.JoinAsync(host.Timeout)).MapId.Should().Be("mine5");

            // Floor 5 has an elevator at its arrival tile; arriving there wrote the depth.
            (await diver.GetMineAsync(host.Timeout)).Should().BeEquivalentTo(new { Status = MineStatus.Ok, Floor = 5, Deepest = 5 });
            (await diver.RideAsync(5, host.Timeout)).Status.Should().Be(MineStatus.AlreadyThere);
            (await diver.RideAsync(10, host.Timeout)).Status.Should().Be(MineStatus.NotUnlocked, "never been that deep");
            (await diver.RideAsync(7, host.Timeout)).Status.Should().Be(MineStatus.NotUnlocked, "not an elevator floor");
            MineState up = await diver.RideAsync(0, host.Timeout);
            up.Should().BeEquivalentTo(new { Status = MineStatus.Ok, Floor = 0, Deepest = 5 });
            (await diver.MapChanges.WaitAsync(m => m.MapId == GameMaps.Mine, host.Timeout)).MapId.Should().Be("mine");

            // The cave's spawn is across the cave from its elevator.
            (await diver.RideAsync(5, host.Timeout)).Status.Should().Be(MineStatus.NotAtElevator);
        }

        // Another day, another session: the depth is in the database, and the elevator is the one in the cave.
        await using (Client again = await host.ConnectAsync(token))
        {
            again.AccountId.Should().Be(playerId);
            await again.PutOnAsync(GameMaps.Mine, (MineWorld.CaveElevator.X + 0.5f) * 16, ((MineWorld.CaveElevator.Y + 2) * 16) - 1);
            (await again.JoinAsync(host.Timeout)).MapId.Should().Be(GameMaps.Mine);

            (await again.GetMineAsync(host.Timeout)).Should().BeEquivalentTo(new { Status = MineStatus.Ok, Floor = 0, Deepest = 5 });
            (await again.RideAsync(0, host.Timeout)).Status.Should().Be(MineStatus.AlreadyThere);
            (await again.RideAsync(10, host.Timeout)).Status.Should().Be(MineStatus.NotUnlocked);
            (await again.RideAsync(5, host.Timeout)).Should().BeEquivalentTo(new { Status = MineStatus.Ok, Floor = 5, Deepest = 5 });
            (await again.MapChanges.WaitAsync(m => m.MapId == "mine5", host.Timeout)).MapId.Should().Be("mine5");
            (await again.GetNodesAsync(host.Timeout)).MapId.Should().Be("mine5");
        }

        // A player who has been nowhere cannot ride anywhere.
        await using Client newcomer = await host.ConnectAsync();
        await newcomer.PutOnAsync(GameMaps.Mine, (MineWorld.CaveElevator.X + 0.5f) * 16, ((MineWorld.CaveElevator.Y + 2) * 16) - 1);
        _ = await newcomer.JoinAsync(host.Timeout);
        (await newcomer.GetMineAsync(host.Timeout)).Deepest.Should().Be(0);
        (await newcomer.RideAsync(5, host.Timeout)).Status.Should().Be(MineStatus.NotUnlocked);
        await using NosumDbContext db = new(_database.Options);
        (await db.PlayerMines.SingleAsync(m => m.PlayerId == playerId)).DeepestFloor.Should().Be(5);
    }

    [Fact]
    public async Task Floor_AnotherPlayersFloor_IsNotVisibleNotEnterableAndNotShared()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client ada = await host.ConnectAsync();
        await using Client bob = await host.ConnectAsync();
        (NodePlacement adaStone, MineFloorPlanner.Plan adaPlan) = await StandNextToAsync(host, ada, floor: 1, n => n.Kind == NodeKind.Stone && n.Variant == 0);
        _ = await StandNextToAsync(host, bob, floor: 1, n => n.Kind == NodeKind.Stone && n.Variant == 0);

        // Same map, two instances: different stones.
        NodeSnapshot adaNodes = await ada.GetNodesAsync(host.Timeout);
        NodeSnapshot bobNodes = await bob.GetNodesAsync(host.Timeout);
        adaNodes.MapId.Should().Be("mine1");
        bobNodes.MapId.Should().Be("mine1");
        // The server sends only the chunks a player is interested in (ADR-022), so ada gets some of her plan, never more.
        adaNodes.Nodes.Select(n => (n.X, n.Y, n.Kind)).Should().NotBeEmpty().And.BeSubsetOf(Planned(adaPlan));
        bobNodes.Nodes.Select(n => (n.X, n.Y, n.Kind)).Should().NotBeEquivalentTo(adaNodes.Nodes.Select(n => (n.X, n.Y, n.Kind)), "a floor is built from the player");

        // Neither sees the other on the map, and a stone ada breaks is not announced to bob.
        MapSnapshot seen = await bob.Snapshots.WaitAsync(_ => true, host.Timeout);
        seen.Players.Should().OnlyContain(p => p.PlayerId == bob.MapPlayerId);
        await ada.UseAsync(await ada.SlotOfAsync(ItemCatalog.Pickaxe), adaStone.X, adaStone.Y, host.Timeout);
        _ = await ada.NodeChanges.WaitAsync(c => c.Removed.Count > 0 || c.Upserted.Count > 0, host.Timeout);
        await bob.NodeChanges.ExpectNothingAsync(TimeSpan.FromMilliseconds(700));

        // The access rule: a floor is its owner's alone, even for somebody the owner let into their farm.
        AccessPolicy access = new(_database);
        (await access.CanEnterAsync(new MapInstance("mine1", ada.AccountId), ada.AccountId)).Should().BeTrue();
        (await access.CanEnterAsync(new MapInstance("mine1", ada.AccountId), bob.AccountId)).Should().BeFalse();
        (await access.CanEnterAsync(new MapInstance("mine1", bob.AccountId), ada.AccountId)).Should().BeFalse();
    }

    [Fact]
    public async Task Floor_ComesBackTheSameSeed_AndAnewTheNextGameDay_WithNothingLeftOnTheGround()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        string token;
        List<(ushort X, ushort Y, byte Kind)> first;
        List<(ushort X, ushort Y, byte Kind)> planned;
        (ushort X, ushort Y, byte Kind) broken;
        Guid playerId;
        await using (Client miner = await host.ConnectAsync())
        {
            token = miner.SessionToken;
            playerId = miner.AccountId;
            (NodePlacement stone, MineFloorPlanner.Plan plan) = await StandNextToAsync(host, miner, floor: 1, n => n.Kind == NodeKind.Stone && n.Variant == 0);
            planned = Planned(plan);
            broken = ((ushort)stone.X, (ushort)stone.Y, (byte)stone.Kind);
            first = [.. (await miner.GetNodesAsync(host.Timeout)).Nodes.Select(n => (n.X, n.Y, n.Kind))];
            first.Should().NotBeEmpty().And.BeSubsetOf(planned);

            (await miner.UseAsync(await miner.SlotOfAsync(ItemCatalog.Pickaxe), stone.X, stone.Y, host.Timeout)).Action.Should().Be(FarmAction.Broke);
            (await miner.GetNodesAsync(host.Timeout)).Nodes.Should().HaveCount(first.Count - 1, "the broken stone leaves the view and nothing else does");
        }

        await host.WaitUntilNobodyIsOnAMapAsync();
        await using (NosumDbContext db = new(_database.Options))
        {
            (await db.GroundItems.CountAsync(g => g.MapId == "mine1")).Should().Be(0, "what lay on the floor went with it");
        }

        // The same day: the same floor, the broken stone is back.
        await using (Client same = await host.ConnectAsync(token))
        {
            same.AccountId.Should().Be(playerId);
            _ = await StandNextToAsync(host, same, floor: 1, n => n.X == broken.X && n.Y == broken.Y);

            // What is in view is of this day's plan, and the broken stone is in it again.
            (await same.GetNodesAsync(host.Timeout)).Nodes.Select(n => (n.X, n.Y, n.Kind)).Should().NotBeEmpty().And.BeSubsetOf(planned).And.Contain(broken);
        }

        await host.WaitUntilNobodyIsOnAMapAsync();

        // The next game day: another floor.
        await using (Client nextDay = await host.ConnectAsync(token))
        {
            (await nextDay.AdvanceClockAsync(1, host.Timeout)).Ok.Should().BeTrue();
            await nextDay.PutOnAsync("mine1", -1, -1);
            _ = await nextDay.JoinAsync(host.Timeout);
            (await nextDay.GetNodesAsync(host.Timeout)).Nodes.Select(n => (n.X, n.Y, n.Kind)).Should().NotBeEquivalentTo(first);
        }
    }
}

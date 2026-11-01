// Copyright (c) 2026 PPN Systems. All rights reserved.
// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007
using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nosum.Abstractions.Maps;
using Nosum.Contracts.Fishing;
using Nosum.Contracts.Fishing.S2C;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Fishing;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The Development QA hook (docs/adr/ADR-079) over real Nalix sessions: the teleport, the season change, the level-up banner and the fishing bite
/// answer in Development and move the server state; a host started as Production does not map their opcodes, so nothing answers them.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class DevQaWireTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = NodeWireTests.s_start;

    private readonly PostgresFixture _database;

    public DevQaWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task DevTeleport_InDevelopment_MovesThePlayerToTheTileAndSavesThePlace()
    {
        FakeTimeProvider clock = new(s_start);
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database, clock);
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        _ = await player.JoinAsync(host.Timeout);
        GameMaps.TryGet(GameMaps.Town, out MapSpec? town).Should().BeTrue();

        (await player.TeleportAsync(GameMaps.Town, town!.SpawnTileX, town.SpawnTileY, host.Timeout)).Should().BeTrue();

        await using NosumDbContext db = new(_database.Options);
        PlayerState state = await db.PlayerStates.SingleAsync(p => p.PlayerId == player.AccountId);
        state.MapName.Should().Be(GameMaps.Town, "the new place is saved like any warp");
    }

    [Fact]
    public async Task DevTeleport_UnknownMapOrTileOffTheMap_IsRefused()
    {
        FakeTimeProvider clock = new(s_start);
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database, clock);
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        _ = await player.JoinAsync(host.Timeout);

        (await player.TeleportAsync("no-such-map", 1, 1, host.Timeout)).Should().BeFalse("an unknown map is refused");
        (await player.TeleportAsync(GameMaps.Town, 4000, 4000, host.Timeout)).Should().BeFalse("a tile far off the map is refused");
    }

    [Fact]
    public async Task DevSeason_InDevelopment_PassesASeasonStart()
    {
        FakeTimeProvider clock = new(s_start);
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database, clock);
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        _ = await player.JoinAsync(host.Timeout);

        (await player.SeasonAsync(1, host.Timeout)).Should().BeTrue();
        (await player.SeasonAsync(2, host.Timeout)).Should().BeTrue("the clock moves on from the season it reached");
    }

    [Fact]
    public async Task DevBefriend_InDevelopment_MakesTheSenderRegisteredAndFriendsWithANewRegisteredPlayer()
    {
        FakeTimeProvider clock = new(s_start);
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database, clock);
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        _ = await player.JoinAsync(host.Timeout);

        (bool ok, string nickname, string token) = await player.BefriendAsync(host.Timeout);

        ok.Should().BeTrue("a Development host makes the test friend");
        nickname.Should().StartWith("QaFriend", "the test player has a registered name of its own");
        token.Should().NotBeNullOrEmpty("a second client signs in as the test player with this token");

        await using NosumDbContext db = new(_database.Options);
        Player sender = await db.Players.SingleAsync(p => p.Id == player.AccountId);
        sender.IsGuest.Should().BeFalse("a friends list needs a registered account on both sides");
        Player friend = await db.Players.SingleAsync(p => p.Nickname == nickname);
        friend.IsGuest.Should().BeFalse("the test player is registered");
        (await db.Friendships.AnyAsync(f => f.PlayerId == sender.Id && f.FriendId == friend.Id)).Should().BeTrue("the friendship is kept on the sender's side");
        (await db.Friendships.AnyAsync(f => f.PlayerId == friend.Id && f.FriendId == sender.Id)).Should().BeTrue("and on the test player's side");
    }

    [Fact]
    public async Task DevLevelUp_InDevelopment_PushesTheBannerToTheSender()
    {
        FakeTimeProvider clock = new(s_start);
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database, clock);
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        _ = await player.JoinAsync(host.Timeout);

        (await player.LevelUpAsync(3, 2, host.Timeout)).Should().BeTrue();
        (byte skill, byte level) = await player.LevelUps.WaitAsync(l => l.Skill == 3 && l.Level == 2, host.Timeout);
        skill.Should().Be(3);
        level.Should().Be(2);
    }

    [Fact]
    public async Task DevFishingBite_InDevelopment_MakesTheOpenCastBiteAtOnce()
    {
        FakeTimeProvider clock = new(s_start);
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database, clock);
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        (int waterX, int waterY, _, _) = await StandOnShoreAsync(host, player);
        await GiveRodAsync(host, player);
        CastResult cast = await player.CastRodAsync(waterX, waterY, host.Timeout);
        cast.BiteDelayMs.Should().BeGreaterThanOrEqualTo(FishingRules.BiteMinMs);
        (await player.ReelInAsync(cast.CastId, host.Timeout)).Status.Should().Be(FishingStatus.TooEarly, "the bite is still to come");

        (await player.BiteAsync(cast.CastId + 1, host.Timeout)).Should().BeFalse("only the sender's own open cast can be made to bite");
        (await player.BiteAsync(cast.CastId, host.Timeout)).Should().BeTrue();

        ReelResult reel = await player.ReelInAsync(cast.CastId, host.Timeout);
        reel.Status.Should().NotBe(FishingStatus.TooEarly, "the bite has come, so the press is in or after the window");
        reel.Status.Should().NotBe(FishingStatus.NoCast);
    }

    [Fact]
    public async Task DevQaOpcodes_InProduction_AreNotMappedAndNothingAnswers()
    {
        FakeTimeProvider clock = new(s_start);
        await using NodeWireTests.TestHost production = await NodeWireTests.TestHost.StartAsync(_database, clock, development: false);
        await using NodeWireTests.ToolClient player = await production.ConnectAsync();
        _ = await player.JoinAsync(production.Timeout);

        (await player.ProbeTeleportAsync(production.Timeout)).Should().BeFalse("Production never maps DevTeleport");
        (await player.ProbeSeasonAsync(production.Timeout)).Should().BeFalse("Production never maps DevSeason");
        (await player.ProbeLevelUpAsync(production.Timeout)).Should().BeFalse("Production never maps DevLevelUp");
        (await player.ProbeBiteAsync(production.Timeout)).Should().BeFalse("Production never maps DevFishingBite");
        (await player.ProbeBefriendAsync(production.Timeout)).Should().BeFalse("Production never maps DevBefriend");
    }

    private static async Task<(int WaterX, int WaterY, int StandX, int StandY)> StandOnShoreAsync(NodeWireTests.TestHost host, NodeWireTests.ToolClient player)
    {
        (int waterX, int waterY, int standX, int standY) = NodeWireTests.ShoreOf(BeachOf());
        await player.PutOnAsync(GameMaps.Beach, (standX * 16) + 8, (standY * 16) + 8);
        _ = await player.JoinAsync(host.Timeout);
        return (waterX, waterY, standX, standY);
    }

    private static async Task GiveRodAsync(NodeWireTests.TestHost host, NodeWireTests.ToolClient player)
    {
        (await player.GrantAsync(ItemCatalog.FishingRod, 1, host.Timeout)).Should().BeTrue();
        InventorySlotRow rod = await player.Slots.WaitAsync(s => s.ItemId == ItemCatalog.FishingRod, host.Timeout);
        (await player.SelectSlotAsync(rod.Slot, host.Timeout)).Should().Be(InventoryStatus.Ok);
    }

    private static MapDefinition BeachOf()
    {
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(GameMaps.Beach, out MapDefinition? beach);
        return beach!;
    }
}

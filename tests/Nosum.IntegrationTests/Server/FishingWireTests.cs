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
using Nosum.Gameplay.Energy;
using Nosum.Gameplay.Fishing;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Casting a fishing rod and reeling in (docs/adr/ADR-071) over real Nalix WebSocket sessions against the real host and PostgreSQL: the refusals of a cast in
/// their order, the energy charged, the bite that comes later than the reel-in, the press that is too early and keeps the cast open, and the press that
/// comes after the window and loses the fish. The clock is a <see cref="FakeTimeProvider"/>, so every timing outcome is exact.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class FishingWireTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = NodeWireTests.s_start;
    private static readonly TimeSpan s_lateBy = TimeSpan.FromMilliseconds(FishingRules.LateToleranceMs + 1);

    private readonly PostgresFixture _database;

    public FishingWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task CastRod_AtBeachWaterWithARod_OpensACastWithABiteInRangeAndChargesTheEnergy()
    {
        FakeTimeProvider clock = new(s_start);
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database, clock);
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        (int waterX, int waterY, _, _) = await StandOnShoreAsync(host, player);
        await GiveRodAsync(host, player);

        CastResult cast = await player.CastRodAsync(waterX, waterY, host.Timeout);

        cast.Status.Should().Be(FishingStatus.Ok);
        cast.CastId.Should().NotBe(0u);
        cast.BiteDelayMs.Should().BeInRange(FishingRules.BiteMinMs, FishingRules.BiteMinMs + FishingRules.BiteSpreadMs - 1);
        cast.WindowMs.Should().Be(FishingRules.WindowMaxMs, "a new fisher is at level zero");
        await using NosumDbContext db = new(_database.Options);
        PlayerState state = await db.PlayerStates.SingleAsync(p => p.PlayerId == player.AccountId);
        state.Energy.Should().Be(EnergyRules.MaxEnergy - FishingRules.CastEnergy);
    }

    [Fact]
    public async Task CastRod_WithoutARod_IsNoRod()
    {
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database, new FakeTimeProvider(s_start));
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        (int waterX, int waterY, _, _) = await StandOnShoreAsync(host, player);

        CastResult cast = await player.CastRodAsync(waterX, waterY, host.Timeout);

        cast.Status.Should().Be(FishingStatus.NoRod);
        cast.CastId.Should().Be(0u);
    }

    [Fact]
    public async Task CastRod_AtDryGroundOnTheShore_IsNotWater()
    {
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database, new FakeTimeProvider(s_start));
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        (_, _, int standX, int standY) = await StandOnShoreAsync(host, player);
        await GiveRodAsync(host, player);

        CastResult cast = await player.CastRodAsync(standX, standY, host.Timeout);

        cast.Status.Should().Be(FishingStatus.NotWater);
    }

    [Fact]
    public async Task CastRod_AtWaterFarFromThePlayer_IsTooFar()
    {
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database, new FakeTimeProvider(s_start));
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        (_, _, int standX, int standY) = await StandOnShoreAsync(host, player);
        await GiveRodAsync(host, player);
        (int farX, int farY) = FarWaterFrom(BeachOf(), standX, standY);

        CastResult cast = await player.CastRodAsync(farX, farY, host.Timeout);

        cast.Status.Should().Be(FishingStatus.TooFar);
    }

    [Fact]
    public async Task ReelIn_BeforeTheBite_IsTooEarlyAndTheCastStaysOpen()
    {
        FakeTimeProvider clock = new(s_start);
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database, clock);
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        (int waterX, int waterY, _, _) = await StandOnShoreAsync(host, player);
        await GiveRodAsync(host, player);
        CastResult cast = await player.CastRodAsync(waterX, waterY, host.Timeout);

        ReelResult early = await player.ReelInAsync(cast.CastId, host.Timeout);
        early.Status.Should().Be(FishingStatus.TooEarly);

        clock.Advance(TimeSpan.FromMilliseconds(cast.BiteDelayMs));
        ReelResult pressed = await player.ReelInAsync(cast.CastId, host.Timeout);
        pressed.Status.Should().BeOneOf(new[] { FishingStatus.Ok, FishingStatus.Nothing }, "the press is in the window, so the roll decides");
        pressed.XpGained.Should().BeOneOf(1, 5, 10, 20, 40);
    }

    [Fact]
    public async Task ReelIn_AfterTheWindowPlusLateTolerance_Escapes()
    {
        FakeTimeProvider clock = new(s_start);
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database, clock);
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        (int waterX, int waterY, _, _) = await StandOnShoreAsync(host, player);
        await GiveRodAsync(host, player);
        CastResult cast = await player.CastRodAsync(waterX, waterY, host.Timeout);

        clock.Advance(TimeSpan.FromMilliseconds(cast.BiteDelayMs + cast.WindowMs) + s_lateBy);
        ReelResult late = await player.ReelInAsync(cast.CastId, host.Timeout);
        late.Status.Should().Be(FishingStatus.Escaped);

        ReelResult again = await player.ReelInAsync(cast.CastId, host.Timeout);
        again.Status.Should().Be(FishingStatus.NoCast, "the escaped cast is over");
    }

    [Fact]
    public async Task ReelIn_UnknownCastId_IsNoCast()
    {
        await using NodeWireTests.TestHost host = await NodeWireTests.TestHost.StartAsync(_database, new FakeTimeProvider(s_start));
        await using NodeWireTests.ToolClient player = await host.ConnectAsync();
        await StandOnShoreAsync(host, player);

        ReelResult reel = await player.ReelInAsync(987_654_321u, host.Timeout);

        reel.Status.Should().Be(FishingStatus.NoCast);
    }

    // Puts the player on the Beach, on dry ground beside the sea, and joins them there. Returns the water tile and the tile they stand on.
    private static async Task<(int WaterX, int WaterY, int StandX, int StandY)> StandOnShoreAsync(NodeWireTests.TestHost host, NodeWireTests.ToolClient player)
    {
        (int waterX, int waterY, int standX, int standY) = NodeWireTests.ShoreOf(BeachOf());
        await player.PutOnAsync(GameMaps.Beach, (standX * 16) + 8, (standY * 16) + 8);
        _ = await player.JoinAsync(host.Timeout);
        return (waterX, waterY, standX, standY);
    }

    // Grants a rod and selects the hotbar slot it landed in, so the rod is the item the player holds.
    private static async Task GiveRodAsync(NodeWireTests.TestHost host, NodeWireTests.ToolClient player)
    {
        (await player.GrantAsync(ItemCatalog.FishingRod, 1, host.Timeout)).Should().BeTrue();
        InventorySlotRow rod = await player.Slots.WaitAsync(s => s.ItemId == ItemCatalog.FishingRod, host.Timeout);
        (await player.SelectSlotAsync(rod.Slot, host.Timeout)).Should().Be(InventoryStatus.Ok);
    }

    // The first water tile at least four tiles from the standing tile, so no reach can count it.
    private static (int X, int Y) FarWaterFrom(MapDefinition map, int standX, int standY)
    {
        for (int y = 0; y < map.Grid.Height; y++)
        {
            for (int x = 0; x < map.Grid.Width; x++)
            {
                int dx = x - standX;
                int dy = y - standY;
                if (map.Water!.IsWater(x, y) && (dx * dx) + (dy * dy) > 16)
                {
                    return (x, y);
                }
            }
        }

        throw new InvalidOperationException("No far water on " + map.Id);
    }

    private static MapDefinition BeachOf()
    {
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(GameMaps.Beach, out MapDefinition? beach);
        return beach!;
    }
}

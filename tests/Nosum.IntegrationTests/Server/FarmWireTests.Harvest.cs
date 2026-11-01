// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Dev.S2C;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.GroundItem.S2C;
using Nosum.Contracts.Player.S2C;
using Nosum.Abstractions.Calendar;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Host;
using Xunit;

namespace Nosum.IntegrationTests.Server;

// A ripe radish waits for its harvest: it is harvested with the scythe over the wire, it survives later dry days, and it survives a season change
// (docs/adr/ADR-021 D7 amended 2026-10-31, docs/adr/ADR-018 D4).
public sealed partial class FarmWireTests
{
    // Starter kit slots (gameplay/Nosum.Gameplay/Inventory/StarterKit.cs): hoe 0, watering can 1, radish seeds 3, scythe 7; slot 8 is empty (bare hands).
    private const int HoeSlot = 0;
    private const int CanSlot = 1;
    private const int RadishSeedSlot = 3;
    private const int ScytheSlot = 7;

    // Radish: six watered game days to ripen (CropCatalog, radish 1/2/1/2).
    private const int RadishWateredDays = 6;

    // One dev clock call moves at most this many game days (IDevClockService.MaxDays).
    private const int MaxDevClockDays = 60;

    [Fact]
    public async Task Harvest_ScytheOnARipeRadish_HarvestsTheCropAndDropsTheProduce()
    {
        await ResetFarmAsync();
        FakeTimeProvider clock = new(s_start);
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        TestAccounts.UseFakeVerifiers(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, clock, development: true);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using FarmClient farmer = await FarmClient.ConnectAsync(port, timeout.Token);
            JoinMapResult join = await farmer.JoinAsync(timeout.Token);
            (int x, int y) = TileInReachOf(join);

            await PlantAndWaterRadishAsync(farmer, clock, x, y, timeout.Token);

            // Day 7: the radish has six watered days behind it and is ripe. The scythe press harvests it.
            clock.Advance(s_gameDay);
            UseItemResult harvested = await farmer.UseAsync(ScytheSlot, x, y, timeout.Token);
            harvested.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Harvested });
            _ = await farmer.WaitForTileAsync(x, y, t => t.CropId == 0 && !t.Removed, timeout.Token);

            GroundItemRow dropped = await farmer.WaitForGroundItemAsync(g => g.ItemId == ItemCatalog.Radish, timeout.Token);
            dropped.Quantity.Should().BeInRange(1, 2);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Harvest_ARipeRadishSurvivesFiveDryDaysAndStillHarvests()
    {
        await ResetFarmAsync();
        FakeTimeProvider clock = new(s_start);
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        TestAccounts.UseFakeVerifiers(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, clock, development: true);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using FarmClient farmer = await FarmClient.ConnectAsync(port, timeout.Token);
            JoinMapResult join = await farmer.JoinAsync(timeout.Token);
            (int x, int y) = TileInReachOf(join);

            await PlantAndWaterRadishAsync(farmer, clock, x, y, timeout.Token);

            // Day 7: ripe, not harvested. Five dry game days follow (no watering, no harvest).
            clock.Advance(s_gameDay);
            for (int day = 0; day < 5; day++)
            {
                clock.Advance(s_gameDay);
            }

            await AssertStandingAndHarvestableAsync(farmer, x, y, timeout.Token);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Harvest_ARipeRadishSurvivesTheWinterSeasonChange_AndStillHarvests()
    {
        await ResetFarmAsync();
        FakeTimeProvider clock = new(s_start);
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        TestAccounts.UseFakeVerifiers(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, clock, development: true);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using FarmClient farmer = await FarmClient.ConnectAsync(port, timeout.Token);
            JoinMapResult join = await farmer.JoinAsync(timeout.Token);
            (int x, int y) = TileInReachOf(join);

            await PlantAndWaterRadishAsync(farmer, clock, x, y, timeout.Token);

            // Planted on day 1 in spring, ripe on day 7. Radish grows in spring, summer and fall, not winter; winter starts on day 85 of the
            // calendar (28 game days a season, docs/adr/ADR-018 D4). The season change used to wither a ripe radish, so it vanished while ripe.
            clock.Advance(s_gameDay);
            (await farmer.AdvanceClockAsync(MaxDevClockDays, timeout.Token)).Ok.Should().BeTrue();
            DevAdvanceClockResult moved = await farmer.AdvanceClockAsync(20, timeout.Token);
            moved.Ok.Should().BeTrue();
            GameCalendar.Default.SeasonAt(moved.ServerNowMs).Should().Be(Season.Winter, "the server clock is past the start of winter");

            await AssertStandingAndHarvestableAsync(farmer, x, y, timeout.Token);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    // Tills the tile, plants a radish seed, and waters it on each of the six days that make it ripe (days 1 to 6 of the clock).
    private static async Task PlantAndWaterRadishAsync(FarmClient farmer, FakeTimeProvider clock, int x, int y, CancellationToken ct)
    {
        (await farmer.UseAsync(HoeSlot, x, y, ct)).Action.Should().Be(FarmAction.Tilled);
        (await farmer.UseAsync(RadishSeedSlot, x, y, ct)).Action.Should().Be(FarmAction.Planted);
        (await farmer.UseAsync(CanSlot, x, y, ct)).Action.Should().Be(FarmAction.Watered);
        for (int day = 2; day <= RadishWateredDays; day++)
        {
            clock.Advance(s_gameDay);
            (await farmer.UseAsync(CanSlot, x, y, ct)).Action.Should().Be(FarmAction.Watered);
        }
    }

    // The ripe radish is on the farm, not withered, and the scythe harvests it.
    private static async Task AssertStandingAndHarvestableAsync(FarmClient farmer, int x, int y, CancellationToken ct)
    {
        FarmSnapshot farm = await farmer.GetFarmAsync(ct);
        FarmTileRow ripe = farm.Tiles.Should().ContainSingle(t => t.X == x && t.Y == y).Subject;
        ripe.CropId.Should().Be(CropCatalog.Radish);
        ripe.Withered.Should().BeFalse("a ripe crop waits for its harvest");

        UseItemResult harvested = await farmer.UseAsync(ScytheSlot, x, y, ct);
        harvested.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Harvested });
    }
}

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
using Nosum.Abstractions.Calendar;
using Nosum.Abstractions.Maps;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.GroundItem;
using Nosum.Contracts.GroundItem.C2S;
using Nosum.Contracts.GroundItem.S2C;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.Quest;
using Nosum.Contracts.Quest.C2S;
using Nosum.Contracts.Quest.S2C;
using Nosum.Contracts.Season.C2S;
using Nosum.Contracts.Season.S2C;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Quests;
using Nosum.Gameplay.Weather;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Farming over real Nalix WebSocket sessions against the real host and a real PostgreSQL database:
/// till, plant, water, fast-forward the server clock, harvest, and a second player on their own farm seeing none of it.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class FarmWireTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);
    private static readonly DateTimeOffset s_start = new(2026, 11, 2, 3, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan s_gameDay = TimeSpan.FromMilliseconds(GameCalendar.DefaultDayLengthMs);

    private readonly PostgresFixture _database;

    public FarmWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Farming_TillPlantWaterWaitHarvest_DropsTheCropBesideTheHarvesterWhoTakesIt()
    {
        await ResetFarmAsync();
        FakeTimeProvider clock = new(s_start);
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, clock);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using FarmClient farmer = await FarmClient.ConnectAsync(port, timeout.Token);
            await using FarmClient neighbour = await FarmClient.ConnectAsync(port, timeout.Token);
            JoinMapResult join = await farmer.JoinAsync(timeout.Token);
            _ = await neighbour.JoinAsync(timeout.Token);
            InventorySnapshot start = await farmer.GetInventoryAsync(timeout.Token);
            (int x, int y) = TileInReachOf(join);

            // The starter kit: hoe, watering can, seeds of three crops.
            start.Capacity.Should().Be(36);
            start.Slots.Select(s => (int)s.ItemId).Should().StartWith([ItemCatalog.Hoe, ItemCatalog.WateringCan, ItemCatalog.ParsnipSeeds]);

            // Planting before tilling is refused.
            (await farmer.UseAsync(2, x, y, timeout.Token)).Status.Should().Be(FarmStatus.NotTilled);

            // Till: the farmer is told, and the neighbour sees the tile.
            UseItemResult tilled = await farmer.UseAsync(0, x, y, timeout.Token);
            tilled.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Tilled });
            await farmer.WaitForTileAsync(x, y, _ => true, timeout.Token);

            // Tilling twice is refused.
            (await farmer.UseAsync(0, x, y, timeout.Token)).Status.Should().Be(FarmStatus.AlreadyTilled);

            // Plant a parsnip seed: one seed leaves the stack, the pushed update says so.
            UseItemResult planted = await farmer.UseAsync(2, x, y, timeout.Token);
            planted.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Planted });
            FarmTileRow seenPlanted = await farmer.WaitForTileAsync(x, y, t => t.CropId == CropCatalog.Parsnip, timeout.Token);
            seenPlanted.PlantedAtMs.Should().Be(s_start.ToUnixTimeMilliseconds());
            InventorySlotRow seedsLeft = await farmer.WaitForSlotAsync(s => s.Slot == 2, timeout.Token);
            (seedsLeft.ItemId, seedsLeft.Quantity).Should().Be((ItemCatalog.ParsnipSeeds, 7));

            // A young crop cannot be harvested; a second seed cannot be planted on it.
            (await farmer.UseAsync(1, x, y, timeout.Token)).Action.Should().Be(FarmAction.Watered);
            (await farmer.UseAsync(2, x, y, timeout.Token)).Status.Should().Be(FarmStatus.HasCrop);
            (await farmer.UseAsync(30, x, y, timeout.Token)).Status.Should().Be(FarmStatus.InvalidSlot);
            (await farmer.UseAsync(8, x, y, timeout.Token)).Status.Should().Be(FarmStatus.NotMature);

            // Four game days, watered each day (docs/adr/ADR-021 D7): the crop is mature when the fourth day has ended, and harvesting works.
            for (int day = 1; day < CropCatalog.All[0].TotalGrowthDays; day++)
            {
                clock.Advance(s_gameDay);
                (await farmer.UseAsync(1, x, y, timeout.Token)).Action.Should().Be(FarmAction.Watered);
                (await farmer.UseAsync(8, x, y, timeout.Token)).Status.Should().Be(FarmStatus.NotMature);
            }

            clock.Advance(s_gameDay);
            UseItemResult harvested = await farmer.UseAsync(8, x, y, timeout.Token);
            harvested.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Harvested });
            _ = await farmer.WaitForTileAsync(x, y, t => t.CropId == 0 && !t.Removed, timeout.Token);

            // The produce pops onto the ground beside the plot (docs/adr/ADR-021 D5), within reach of the harvester, who takes it
            // at once without stepping (D6).
            GroundItemRow dropped = await farmer.WaitForGroundItemAsync(g => g.ItemId == ItemCatalog.Parsnip, timeout.Token);
            dropped.Quantity.Should().Be(1);
            InventorySlotRow produce = await farmer.WaitForSlotAsync(s => s.ItemId == ItemCatalog.Parsnip, timeout.Token);
            produce.Quantity.Should().Be(1);
            (await farmer.GetInventoryAsync(timeout.Token)).Slots.Should().Contain(s => s.ItemId == ItemCatalog.Parsnip && s.Quantity == 1);
            (await farmer.GetGroundItemsAsync(timeout.Token)).Items.Should().BeEmpty();

            // The tutorial quest is complete and the guest is invited to register.
            QuestRow quest = await farmer.WaitForQuestAsync(q => q.State == QuestState.Completed, timeout.Token);
            quest.QuestId.Should().Be(QuestCatalog.FirstHarvest);
            quest.Progress.Should().Equal(1, 1);
            await farmer.WaitForPromptUnlockedAsync(timeout.Token);
            (await farmer.GetQuestsAsync(timeout.Token)).Quests.Should().ContainSingle().Which.State.Should().Be(QuestState.Completed);

            // The farm is private (docs/adr/ADR-015): the neighbour on their own farm never saw any of it, and their own farm is bare.
            await neighbour.ExpectNoTilePushAsync(TimeSpan.FromMilliseconds(300), timeout.Token);
            (await neighbour.GetFarmAsync(timeout.Token)).Tiles.Should().BeEmpty();

            // The same player signing in again reads their farm.
            await using FarmClient latecomer = await FarmClient.ResumeAsync(port, farmer.SessionToken, timeout.Token);
            _ = await latecomer.JoinAsync(timeout.Token);
            FarmSnapshot farm = await latecomer.GetFarmAsync(timeout.Token);
            farm.MapId.Should().Be(Program.FarmMapId);
            farm.ServerNowMs.Should().Be(clock.GetUtcNow().ToUnixTimeMilliseconds());
            farm.Tiles.Should().ContainSingle(t => t.X == x && t.Y == y).Which.CropId.Should().Be(0);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Seasons_OutOfSeasonSeedIsRefused_ACropWithersAtTheSeasonChange_AndTheSoilCanBeReplanted()
    {
        await ResetFarmAsync();
        FakeTimeProvider clock = new(s_start);
        GameCalendar calendar = GameCalendar.Default;
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, clock);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using FarmClient farmer = await FarmClient.ConnectAsync(port, timeout.Token);
            JoinMapResult join = await farmer.JoinAsync(timeout.Token);
            (int x, int y) = TileInReachOf(join);

            // The client gets the calendar as two numbers and the server clock, and computes the rest itself.
            CalendarInfo info = await farmer.GetCalendarAsync(timeout.Token);
            (info.EpochMs, info.DayLengthMs, info.ServerNowMs).Should().Be((calendar.EpochMs, calendar.DayLengthMs, s_start.ToUnixTimeMilliseconds()));

            (await farmer.UseAsync(0, x, y, timeout.Token)).Action.Should().Be(FarmAction.Tilled);

            // Summer: parsnip seeds (slot 2) cannot be planted, and the seed stays in the stack; radish seeds (slot 3) are planted.
            clock.Advance(TimeSpan.FromMilliseconds(calendar.EpochMs + calendar.SeasonLengthMs + 3_600_000 - clock.GetUtcNow().ToUnixTimeMilliseconds()));
            // Two dry weeks may have reverted the bare soil (docs/adr/ADR-021 D7): till it again if so.
            _ = await farmer.UseAsync(0, x, y, timeout.Token);
            (await farmer.UseAsync(2, x, y, timeout.Token)).Status.Should().Be(FarmStatus.OutOfSeason);
            (await farmer.UseAsync(3, x, y, timeout.Token)).Action.Should().Be(FarmAction.Planted);

            // Fall still suits radish; winter does not: the crop withered, cannot be harvested, and the soil takes a winter crop.
            clock.Advance(TimeSpan.FromMilliseconds(calendar.SeasonLengthMs));
            (await farmer.UseAsync(8, x, y, timeout.Token)).Status.Should().NotBe(FarmStatus.Withered);
            clock.Advance(TimeSpan.FromMilliseconds(calendar.SeasonLengthMs));
            (await farmer.UseAsync(8, x, y, timeout.Token)).Status.Should().Be(FarmStatus.Withered);
            (await farmer.UseAsync(2, x, y, timeout.Token)).Action.Should().Be(FarmAction.Planted);
            (await farmer.WaitForTileAsync(x, y, t => t.CropId == CropCatalog.Parsnip, timeout.Token)).PlantedAtMs.Should().Be(clock.GetUtcNow().ToUnixTimeMilliseconds());
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Growth_WaterDayOne_SkipDayTwo_WaterDayThree_ShowsTheStageOfTwoWateredDays()
    {
        await ResetFarmAsync();
        FakeTimeProvider clock = new(s_start);
        GameCalendar calendar = GameCalendar.Default;
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, clock);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using FarmClient farmer = await FarmClient.ConnectAsync(port, timeout.Token);
            JoinMapResult join = await farmer.JoinAsync(timeout.Token);
            (int x, int y) = TileInReachOf(join);
            (await farmer.UseAsync(0, x, y, timeout.Token)).Action.Should().Be(FarmAction.Tilled);
            (await farmer.UseAsync(2, x, y, timeout.Token)).Action.Should().Be(FarmAction.Planted);

            // Day 1: watered. Day 2: skipped. Day 3: watered. Day 4: two days of growth, a stage-two parsnip.
            (await farmer.UseAsync(1, x, y, timeout.Token)).Action.Should().Be(FarmAction.Watered);
            clock.Advance(s_gameDay);
            clock.Advance(s_gameDay);
            (await farmer.UseAsync(1, x, y, timeout.Token)).Action.Should().Be(FarmAction.Watered);

            FarmTileRow onDayThree = (await farmer.GetFarmAsync(timeout.Token)).Tiles.Single(t => t.X == x && t.Y == y);
            ToTile(onDayThree).Should().Be(new FarmTile(CropCatalog.Parsnip, s_start.ToUnixTimeMilliseconds(), 1, 3, 0));
            FarmRules.StageAt(ToTile(onDayThree), clock.GetUtcNow().ToUnixTimeMilliseconds(), calendar).Should().Be(1, "only day one has ended");

            clock.Advance(s_gameDay);
            FarmSnapshot onDayFour = await farmer.GetFarmAsync(timeout.Token);
            FarmRules.StageAt(ToTile(onDayFour.Tiles.Single(t => t.X == x && t.Y == y)), onDayFour.ServerNowMs, calendar).Should().Be(2);
            (await farmer.UseAsync(8, x, y, timeout.Token)).Status.Should().Be(FarmStatus.NotMature);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Rain_OnAnUnwateredOutdoorCrop_CountsAsAWateredDayOnTheServer()
    {
        await ResetFarmAsync();
        GameCalendar calendar = GameCalendar.Default;
        int needed = CropCatalog.All[0].TotalGrowthDays;

        // The first rainy day that leaves the parsnip's whole growth inside one spring-like stretch (never summer, never across a season change).
        int rainy = Enumerable.Range(3, 400).First(day =>
        {
            int dayOfSeason = ((day - 1) % GameCalendar.DaysPerSeason) + 1;
            Season season = (Season)(((day - 1) / GameCalendar.DaysPerSeason) % GameCalendar.SeasonsPerYear);
            return WeatherRules.WatersOn(calendar, day) && season != Season.Summer && dayOfSeason + needed <= GameCalendar.DaysPerSeason;
        });
        FakeTimeProvider clock = new(s_start);
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, clock);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using FarmClient farmer = await FarmClient.ConnectAsync(port, timeout.Token);
            JoinMapResult join = await farmer.JoinAsync(timeout.Token);
            (int x, int y) = TileInReachOf(join);

            // Till and plant on the rainy day and never water that day: the rain is the only water it gets.
            clock.Advance(TimeSpan.FromTicks(s_gameDay.Ticks * (rainy - 1)));
            (await farmer.UseAsync(0, x, y, timeout.Token)).Action.Should().Be(FarmAction.Tilled);
            (await farmer.UseAsync(2, x, y, timeout.Token)).Action.Should().Be(FarmAction.Planted);

            // Water by hand on each later day. After needed - 1 more days the crop has the rain day plus needed - 2 hand days: one short.
            for (int day = 1; day < needed - 1; day++)
            {
                clock.Advance(s_gameDay);
                (await farmer.UseAsync(1, x, y, timeout.Token)).Action.Should().Be(FarmAction.Watered);
            }

            clock.Advance(s_gameDay);
            (await farmer.UseAsync(1, x, y, timeout.Token)).Action.Should().Be(FarmAction.Watered);
            (await farmer.UseAsync(8, x, y, timeout.Token)).Status.Should().Be(FarmStatus.NotMature, "the last watered day has not ended");

            // One more day ends: the rain day counted once, so the crop is mature and the server lets it be harvested.
            clock.Advance(s_gameDay);
            (await farmer.UseAsync(8, x, y, timeout.Token)).Action.Should().Be(FarmAction.Harvested);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Regrow_AStrawberryPlantIsHarvestedTwiceFromOneSeed_AndOnlyBearsAgainAfterItsRegrowDays()
    {
        await ResetFarmAsync();
        FakeTimeProvider clock = new(s_start);
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, clock, development: true);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(90));
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using FarmClient farmer = await FarmClient.ConnectAsync(port, timeout.Token);
            JoinMapResult join = await farmer.JoinAsync(timeout.Token);
            (int x, int y) = TileInReachOf(join);
            CropDefinition berry = CropCatalog.All.Single(c => c.Id == CropCatalog.Strawberry);
            (await farmer.GrantAsync(ItemCatalog.StrawberrySeeds, 1, timeout.Token)).Should().BeTrue();
            int seeds = (await farmer.GetInventoryAsync(timeout.Token)).Slots.Single(s => s.ItemId == ItemCatalog.StrawberrySeeds).Slot;
            seeds.Should().Be(8, "the first free hotbar slot; the slot is empty again once the seed is planted, so a bare hand harvests from it");

            // Spring: a strawberry seed is planted and watered every day until it bears.
            (await farmer.UseAsync(0, x, y, timeout.Token)).Action.Should().Be(FarmAction.Tilled);
            (await farmer.UseAsync(seeds, x, y, timeout.Token)).Action.Should().Be(FarmAction.Planted);
            for (int day = 0; day < berry.TotalGrowthDays; day++)
            {
                (await farmer.UseAsync(1, x, y, timeout.Token)).Action.Should().Be(FarmAction.Watered);
                (await farmer.AdvanceClockAsync(1, timeout.Token)).Ok.Should().BeTrue();
            }

            // First harvest: the berries drop, the plant stays, back at its regrow stage.
            (await farmer.UseAsync(seeds, x, y, timeout.Token)).Action.Should().Be(FarmAction.Harvested);
            (await farmer.WaitForGroundItemAsync(i => i.ItemId == ItemCatalog.Strawberry, timeout.Token)).Quantity.Should().Be(berry.HarvestQuantity);
            FarmTileRow standing = (await farmer.GetFarmAsync(timeout.Token)).Tiles.Single(t => t.X == x && t.Y == y);
            standing.CropId.Should().Be(CropCatalog.Strawberry, "the plant is still there");
            standing.GrowthDays.Should().Be(berry.GrowthDaysAfterHarvest);
            (await farmer.UseAsync(seeds, x, y, timeout.Token)).Status.Should().Be(FarmStatus.NotMature, "it has to be watered again first");

            // Regrow days of watering, then the second harvest, with no new seed.
            for (int day = 0; day < berry.RegrowDays; day++)
            {
                (await farmer.UseAsync(seeds, x, y, timeout.Token)).Status.Should().Be(FarmStatus.NotMature, $"{day} of {berry.RegrowDays} regrow days");
                (await farmer.UseAsync(1, x, y, timeout.Token)).Action.Should().Be(FarmAction.Watered);
                (await farmer.AdvanceClockAsync(1, timeout.Token)).Ok.Should().BeTrue();
            }

            (await farmer.UseAsync(seeds, x, y, timeout.Token)).Action.Should().Be(FarmAction.Harvested);
            (await farmer.WaitForGroundItemAsync(i => i.ItemId == ItemCatalog.Strawberry, timeout.Token)).Quantity.Should().Be(berry.HarvestQuantity);
            (await farmer.GetFarmAsync(timeout.Token)).Tiles.Single(t => t.X == x && t.Y == y).CropId.Should().Be(CropCatalog.Strawberry, "and it can bear a third time");
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DevClock_FastForward_GrowsTheCropAndRevertsDrySoilOnTheClientsMap()
    {
        await ResetFarmAsync();
        FakeTimeProvider clock = new(s_start);
        GameCalendar calendar = GameCalendar.Default;
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, clock, development: true);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using FarmClient farmer = await FarmClient.ConnectAsync(port, timeout.Token);
            JoinMapResult join = await farmer.JoinAsync(timeout.Token);
            (int x, int y) = NearbyTiles(join).Where(t => IsTillable(t)).Skip(0).First();
            (int dx, int dy) = NearbyTiles(join).Where(t => IsTillable(t) && t != (x, y)).First();

            // Bad requests change nothing.
            (await farmer.AdvanceClockAsync(0, timeout.Token)).Ok.Should().BeFalse();
            (await farmer.AdvanceClockAsync(IDevClockService.MaxDays + 1, timeout.Token)).Ok.Should().BeFalse();

            // A planted, watered parsnip and an untouched tilled tile.
            (await farmer.UseAsync(0, x, y, timeout.Token)).Action.Should().Be(FarmAction.Tilled);
            (await farmer.UseAsync(2, x, y, timeout.Token)).Action.Should().Be(FarmAction.Planted);
            (await farmer.UseAsync(0, dx, dy, timeout.Token)).Action.Should().Be(FarmAction.Tilled);

            // Water on each of the four days, jumping one game day at a time.
            for (int day = 1; day <= CropCatalog.All[0].TotalGrowthDays; day++)
            {
                (await farmer.UseAsync(1, x, y, timeout.Token)).Action.Should().Be(FarmAction.Watered);
                DevAdvanceClockResult moved = await farmer.AdvanceClockAsync(1, timeout.Token);
                moved.Ok.Should().BeTrue();
                moved.ServerNowMs.Should().Be(s_start.ToUnixTimeMilliseconds() + (day * calendar.DayLengthMs));
            }

            // Harvest the mature parsnip; its soil was watered every day, so it is still there.
            (await farmer.UseAsync(8, x, y, timeout.Token)).Action.Should().Be(FarmAction.Harvested);

            // A month without watering: the untouched tile reverts (0.7^59 of surviving), is removed from the client's map and from PostgreSQL.
            (await farmer.AdvanceClockAsync(IDevClockService.MaxDays, timeout.Token)).Ok.Should().BeTrue();
            FarmTileRow gone = await farmer.WaitForTileAsync(dx, dy, t => t.Removed, timeout.Token);
            gone.CropId.Should().Be(0);
            (await farmer.GetFarmAsync(timeout.Token)).Tiles.Should().NotContain(t => t.X == dx && t.Y == dy);

            await using NosumDbContext context = new(_database.Options);
            (await context.FarmPlots.AnyAsync(p => p.X == dx && p.Y == dy)).Should().BeFalse();

            // The player can till the reverted tile again.
            (await farmer.UseAsync(0, dx, dy, timeout.Token)).Action.Should().Be(FarmAction.Tilled);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DevClock_InProduction_IsNotAnswered()
    {
        FakeTimeProvider clock = new(s_start);
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, clock);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using FarmClient farmer = await FarmClient.ConnectAsync(port, timeout.Token);

            Func<Task> advance = async () => _ = await farmer.AdvanceClockAsync(1, timeout.Token, TimeSpan.FromMilliseconds(1500));

            _ = await advance.Should().ThrowAsync<Exception>();
            clock.GetUtcNow().Should().Be(s_start);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    private static FarmTile ToTile(FarmTileRow row) => new(row.CropId, row.PlantedAtMs, row.GrowthDays, row.WateredDay, 0);

    private static bool IsTillable((int X, int Y) tile)
    {
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(Program.FarmMapId, out MapDefinition? map);
        return map!.Tillable!.IsTillable(tile.X, tile.Y);
    }

    [Fact]
    public async Task UseItem_FarFromTheTile_IsRefusedAndNothingChanges()
    {
        await ResetFarmAsync();
        FakeTimeProvider clock = new(s_start);
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, clock);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using FarmClient farmer = await FarmClient.ConnectAsync(port, timeout.Token);
            JoinMapResult join = await farmer.JoinAsync(timeout.Token);
            (int x, int y) = TileInReachOf(join);

            (await farmer.UseAsync(0, x + 12, y, timeout.Token)).Status.Should().Be(FarmStatus.OutOfReach);
            (await farmer.UseAsync(0, -1, y, timeout.Token)).Status.Should().Be(FarmStatus.InvalidTile);
            (await farmer.GetFarmAsync(timeout.Token)).Tiles.Should().BeEmpty();
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task UseItem_HoeOnGroundThatCannotBeTilled_IsRefused()
    {
        await ResetFarmAsync();
        FakeTimeProvider clock = new(s_start);
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, clock);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using FarmClient farmer = await FarmClient.ConnectAsync(port, timeout.Token);
            JoinMapResult join = await farmer.JoinAsync(timeout.Token);
            IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
            _ = maps.TryGet(Program.FarmMapId, out MapDefinition? map);
            (int x, int y) = NearbyTiles(join).First(t => !map!.Tillable!.IsTillable(t.X, t.Y) && map.Tillable.Contains(t.X, t.Y));

            (await farmer.UseAsync(0, x, y, timeout.Token)).Status.Should().Be(FarmStatus.NotTillable);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Farm_AfterAServerRestart_ReturnsTheStoredTilesAndKeepsTheCropGrowing()
    {
        await ResetFarmAsync();
        FakeTimeProvider clock = new(s_start);
        string token;
        int x;
        int y;
        ushort firstPort = GetFreePort();
        HostServices.Register(_database.Options);
        await using (NetworkApplication first = Program.BuildApplication(firstPort, clock))
        {
            using CancellationTokenSource timeout = new(s_timeout);
            await first.ActivateAsync(timeout.Token);
            try
            {
                await using FarmClient farmer = await FarmClient.ConnectAsync(firstPort, timeout.Token);
                token = farmer.SessionToken;
                JoinMapResult join = await farmer.JoinAsync(timeout.Token);
                (x, y) = TileInReachOf(join);
                (await farmer.UseAsync(0, x, y, timeout.Token)).Action.Should().Be(FarmAction.Tilled);
                (await farmer.UseAsync(1, x, y, timeout.Token)).Action.Should().Be(FarmAction.Watered);
                (await farmer.UseAsync(2, x, y, timeout.Token)).Action.Should().Be(FarmAction.Planted);
            }
            finally
            {
                await first.DeactivateAsync(CancellationToken.None);
            }
        }

        ushort secondPort = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication second = Program.BuildApplication(secondPort, clock);
        using CancellationTokenSource timeout2 = new(s_timeout);
        await second.ActivateAsync(timeout2.Token);
        try
        {
            await using FarmClient again = await FarmClient.ResumeAsync(secondPort, token, timeout2.Token);
            _ = await again.JoinAsync(timeout2.Token);

            FarmSnapshot farm = await again.GetFarmAsync(timeout2.Token);
            FarmTileRow tile = farm.Tiles.Should().ContainSingle(t => t.X == x && t.Y == y).Subject;
            tile.CropId.Should().Be(CropCatalog.Parsnip);

            // Planted and watered on game day 1 before the restart; three more watered days and the fourth rollover make it mature.
            for (int day = 2; day <= CropCatalog.All[0].TotalGrowthDays; day++)
            {
                clock.Advance(s_gameDay);
                (await again.UseAsync(1, x, y, timeout2.Token)).Action.Should().Be(FarmAction.Watered);
            }

            clock.Advance(s_gameDay);
            (await again.UseAsync(8, x, y, timeout2.Token)).Action.Should().Be(FarmAction.Harvested);
        }
        finally
        {
            await second.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task UseItem_BeforeSignIn_IsNotAnswered()
    {
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, new FakeTimeProvider(s_start));
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            using WebSocketSession session = new(
                new TransportOptions { Address = "127.0.0.1", Port = port },
                new WebSocketTransportOptions { Path = Program.WebSocketPath });
            await session.ConnectAsync(ct: timeout.Token);
            await session.HandshakeAsync(timeout.Token);
            using UseItem request = new() { Slot = 0, TileX = 1, TileY = 1 };

            Func<Task> use = async () => _ = await session.RequestAsync<UseItemResult>(request, RequestOptions.Default.WithTimeout(1500), ct: timeout.Token);

            _ = await use.Should().ThrowAsync<Exception>();
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    // The tests share one database: start each with an untouched farm.
    private async Task ResetFarmAsync()
    {
        await using NosumDbContext context = new(_database.Options);
        _ = await context.FarmPlots.ExecuteDeleteAsync();
    }

    // Tiles whose centre is within reach of the spawn feet, nearest first.
    private static IEnumerable<(int X, int Y)> NearbyTiles(JoinMapResult join)
    {
        int tx = (int)(join.SpawnX / 16);
        int ty = (int)(join.SpawnY / 16);
        return Enumerable.Range(-2, 5)
            .SelectMany(dx => Enumerable.Range(-2, 5).Select(dy => (X: tx + dx, Y: ty + dy)))
            .Where(t => FarmRules.InReach(join.SpawnX, join.SpawnY, t.X, t.Y, 16))
            .OrderBy(t => Math.Abs(((t.X + 0.5) * 16) - join.SpawnX) + Math.Abs(((t.Y + 0.5) * 16) - join.SpawnY));
    }

    private static (int X, int Y) TileInReachOf(JoinMapResult join)
    {
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(Program.FarmMapId, out MapDefinition? map);
        return NearbyTiles(join).First(t => map!.Tillable!.IsTillable(t.X, t.Y));
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private sealed class FarmClient : IAsyncDisposable
    {
        private static readonly TimeSpan s_useCooldown = TimeSpan.FromMilliseconds(330);

        private readonly WebSocketSession _session;
        private readonly IDisposable[] _subscriptions;
        private readonly Inbox<FarmTileRow> _tiles = new();
        private readonly Inbox<InventorySlotRow> _slots = new();
        private readonly Inbox<QuestRow> _quests = new();
        private readonly Inbox<bool> _prompt = new();
        private readonly Inbox<GroundItemRow> _spawned = new();
        private readonly Inbox<GroundItemTaken> _taken = new();
        private uint _moveSequence;

        private FarmClient(WebSocketSession session)
        {
            _session = session;
            _subscriptions =
            [
                session.On<FarmTilesChanged>(p =>
                {
                    foreach (FarmTileRow t in p.Tiles)
                    {
                        _tiles.Add(new FarmTileRow { X = t.X, Y = t.Y, CropId = t.CropId, PlantedAtMs = t.PlantedAtMs, GrowthDays = t.GrowthDays, WateredDay = t.WateredDay, Removed = t.Removed });
                    }
                }),
                session.On<InventoryUpdate>(p =>
                {
                    foreach (InventorySlotRow s in p.Slots)
                    {
                        _slots.Add(new InventorySlotRow { Slot = s.Slot, ItemId = s.ItemId, Quantity = s.Quantity });
                    }
                }),
                session.On<QuestUpdate>(p => _quests.Add(new QuestRow { QuestId = p.Quest.QuestId, State = p.Quest.State, Progress = [.. p.Quest.Progress] })),
                session.On<RegistrationPromptUnlocked>(_ => _prompt.Add(true)),
                session.On<GroundItemSpawned>(p => _spawned.Add(new GroundItemRow { Id = p.Item.Id, ItemId = p.Item.ItemId, Quantity = p.Item.Quantity, X = p.Item.X, Y = p.Item.Y })),
                session.On<GroundItemTaken>(p => _taken.Add(new GroundItemTaken { Id = p.Id, Reason = p.Reason, ToX = p.ToX, ToY = p.ToY })),
            ];
        }

        public string SessionToken { get; private set; } = string.Empty;

        public static async Task<FarmClient> ConnectAsync(ushort port, CancellationToken ct)
        {
            FarmClient client = await OpenAsync(port, ct);
            using StartGuest request = new() { PreferredLanguage = Nosum.Contracts.Common.GameLanguage.English };
            AccountResult result = await client._session.RequestAsync<AccountResult>(request, ct: ct);
            result.Status.Should().Be(AccountStatus.Ok);
            client.SessionToken = result.SessionToken;
            return client;
        }

        public static async Task<FarmClient> ResumeAsync(ushort port, string token, CancellationToken ct)
        {
            FarmClient client = await OpenAsync(port, ct);
            using ResumeSession request = new() { SessionToken = token };
            AccountResult result = await client._session.RequestAsync<AccountResult>(request, ct: ct);
            result.Status.Should().Be(AccountStatus.Ok);
            client.SessionToken = token;
            return client;
        }

        public async Task<JoinMapResult> JoinAsync(CancellationToken ct)
        {
            using JoinMap request = new() { MapId = Program.FarmMapId };
            JoinMapResult result = await _session.RequestAsync<JoinMapResult>(request, ct: ct);
            result.Status.Should().Be(JoinStatus.Ok);
            return result;
        }

        public async Task<CalendarInfo> GetCalendarAsync(CancellationToken ct)
        {
            using GetCalendar request = new();
            return await _session.RequestAsync<CalendarInfo>(request, ct: ct);
        }

        public async Task<InventorySnapshot> GetInventoryAsync(CancellationToken ct)
        {
            using GetInventory request = new();
            return await _session.RequestAsync<InventorySnapshot>(request, ct: ct);
        }

        public async Task<FarmSnapshot> GetFarmAsync(CancellationToken ct)
        {
            using GetFarm request = new();
            return await _session.RequestAsync<FarmSnapshot>(request, ct: ct);
        }

        public async Task<bool> GrantAsync(int itemId, int quantity, CancellationToken ct)
        {
            using DevGrant request = new() { ItemId = itemId, Quantity = quantity };
            return (await _session.RequestAsync<DevGrantResult>(request, ct: ct)).Ok;
        }

        public async Task<DevAdvanceClockResult> AdvanceClockAsync(int days, CancellationToken ct, TimeSpan? limit = null)
        {
            using DevAdvanceClock request = new() { Days = days };
            RequestOptions options = RequestOptions.Default.WithEncrypt();
            return await _session.RequestAsync<DevAdvanceClockResult>(request, limit is { } wait ? options.WithTimeout((int)wait.TotalMilliseconds) : options, ct: ct);
        }

        public async Task<GroundItemsSnapshot> GetGroundItemsAsync(CancellationToken ct)
        {
            using GetGroundItems request = new();
            return await _session.RequestAsync<GroundItemsSnapshot>(request, ct: ct);
        }

        public Task<GroundItemRow> WaitForGroundItemAsync(Func<GroundItemRow, bool> predicate, CancellationToken ct) => _spawned.WaitAsync(predicate, ct);

        // Walks in small legal steps (the server checks speed) until the item is reported taken, as the real client's 10 Hz reports would.
        public async Task WalkUntilPickedUpAsync(JoinMapResult join, long itemId, CancellationToken ct)
        {
            Task<GroundItemTaken> taken = _taken.WaitAsync(t => t.Id == itemId, ct);
            float x = join.SpawnX;
            for (int step = 0; step < 40 && !taken.IsCompleted; step++)
            {
                x += step % 2 == 0 ? 1f : -1f;
                using MoveIntent intent = new() { Sequence = ++_moveSequence, X = x, Y = join.SpawnY, Facing = 1, Moving = true };
                await _session.SendAsync(intent, ct);
                _ = await Task.WhenAny(taken, Task.Delay(120, ct));
            }

            (await taken).Reason.Should().Be(GroundItemGone.PickedUp);
        }

        public async Task<QuestLog> GetQuestsAsync(CancellationToken ct)
        {
            using GetQuests request = new();
            return await _session.RequestAsync<QuestLog>(request, ct: ct);
        }

        // Like the real client, wait out the handler's rate limit (8 per second per address, burst 3): tests share one address.
        public async Task<UseItemResult> UseAsync(int slot, int x, int y, CancellationToken ct)
        {
            await Task.Delay(s_useCooldown, ct);
            using UseItem request = new() { Slot = (byte)slot, TileX = x, TileY = y };
            return await _session.RequestAsync<UseItemResult>(request, ct: ct);
        }

        // Nothing must arrive: a push meant for another player's farm would leak here.
        public async Task ExpectNoTilePushAsync(TimeSpan wait, CancellationToken ct)
        {
            using CancellationTokenSource shortWait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            shortWait.CancelAfter(wait);
            Func<Task> next = async () => _ = await _tiles.WaitAsync(_ => true, shortWait.Token);
            _ = await next.Should().ThrowAsync<OperationCanceledException>();
        }

        public Task<FarmTileRow> WaitForTileAsync(int x, int y, Func<FarmTileRow, bool> predicate, CancellationToken ct)
            => _tiles.WaitAsync(t => t.X == x && t.Y == y && predicate(t), ct);

        public Task<InventorySlotRow> WaitForSlotAsync(Func<InventorySlotRow, bool> predicate, CancellationToken ct) => _slots.WaitAsync(predicate, ct);

        public Task<QuestRow> WaitForQuestAsync(Func<QuestRow, bool> predicate, CancellationToken ct) => _quests.WaitAsync(predicate, ct);

        public Task<bool> WaitForPromptUnlockedAsync(CancellationToken ct) => _prompt.WaitAsync(_ => true, ct);

        public async ValueTask DisposeAsync()
        {
            foreach (IDisposable subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            await _session.DisconnectAsync();
            _session.Dispose();
        }

        private static async Task<FarmClient> OpenAsync(ushort port, CancellationToken ct)
        {
            WebSocketSession session = new(
                new TransportOptions { Address = "127.0.0.1", Port = port },
                new WebSocketTransportOptions { Path = Program.WebSocketPath });
            FarmClient client = new(session);
            await session.ConnectAsync(ct: ct);
            await session.HandshakeAsync(ct);
            return client;
        }
    }

    // Pushes arrive on socket threads; tests read them in order and skip what does not match.
    private sealed class Inbox<T>
    {
        private readonly Channel<T> _items = Channel.CreateUnbounded<T>();

        public void Add(T item) => _ = _items.Writer.TryWrite(item);

        public async Task<T> WaitAsync(Func<T, bool> predicate, CancellationToken ct)
        {
            while (true)
            {
                T item = await _items.Reader.ReadAsync(ct);
                if (predicate(item))
                {
                    return item;
                }
            }
        }
    }
}

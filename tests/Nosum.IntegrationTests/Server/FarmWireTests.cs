// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Abstractions.Calendar;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
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
using Nosum.Gameplay.Maps;
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
public sealed partial class FarmWireTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);
    private static readonly DateTimeOffset s_start = new(2026, 11, 2, 3, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan s_gameDay = TimeSpan.FromMilliseconds(GameCalendar.DefaultDayLengthMs);

    private readonly PostgresFixture _database;

    public FarmWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Farming_UseSentSoonerThanTheInterval_IsRefusedWithCooldownAndChangesNothing()
    {
        await ResetFarmAsync();
        FakeTimeProvider clock = new(s_start);
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        TestAccounts.UseFakeVerifiers(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, clock);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using FarmClient farmer = await FarmClient.ConnectAsync(port, timeout.Token);
            JoinMapResult join = await farmer.JoinAsync(timeout.Token);
            (int x, int y) = TileInReachOf(join);

            // The first use lands: the hoe tills the tile.
            (await farmer.UseAsync(0, x, y, timeout.Token)).Status.Should().Be(FarmStatus.Ok);

            // A second use with no wait comes inside the server's interval (docs/adr/ADR-074 D2): refused, with no reply text, and the tile is untouched.
            (await farmer.UseWithoutWaitAsync(0, x, y, timeout.Token)).Status.Should().Be(FarmStatus.Cooldown);

            // Once the interval has passed the same hoe is told what the tile already is, which proves the refused use changed nothing.
            (await farmer.UseAsync(0, x, y, timeout.Token)).Status.Should().Be(FarmStatus.AlreadyTilled);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WideUse_ABasicHoeCannotWorkALine_AnUpgradedHoeReportsTheTilesItWorked()
    {
        await ResetFarmAsync();
        FakeTimeProvider clock = new(s_start);
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        TestAccounts.UseFakeVerifiers(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, clock);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using FarmClient farmer = await FarmClient.ConnectAsync(port, timeout.Token);
            JoinMapResult join = await farmer.JoinAsync(timeout.Token);
            (int x, int y) = TileInReachOf(join);

            // The basic hoe works one tile: a line asked of it is refused, and nothing is worked.
            UseItemResult basic = await farmer.UseAreaAsync(0, x, y, 3, timeout.Token);
            basic.Should().BeEquivalentTo(new { Status = FarmStatus.InvalidArea, Worked = (byte)0 });
            (await farmer.GetFarmAsync(timeout.Token)).Tiles.Should().BeEmpty();

            // A copper hoe works a line of three. The reply counts the tiles that worked, and the farm holds exactly those tiles:
            // each of them was charged its own energy (docs/adr/ADR-080), so the count is what the client shows as the energy spent.
            await MineWireTests.GrantAsync(farmer.AccountId, ItemCatalog.CopperHoe, 1, 0);
            InventorySlotRow copper = (await farmer.GetInventoryAsync(timeout.Token)).Slots.Single(s => s.ItemId == ItemCatalog.CopperHoe);
            UseItemResult wide = await farmer.UseAreaAsync(copper.Slot, x, y, 3, timeout.Token);
            wide.Status.Should().Be(FarmStatus.Ok);
            wide.Action.Should().Be(FarmAction.Tilled);
            wide.Worked.Should().BeInRange((byte)1, (byte)3);
            (await farmer.GetFarmAsync(timeout.Token)).Tiles.Should().HaveCount(wide.Worked);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Farming_TillPlantWaterWaitHarvest_DropsTheCropBesideTheHarvesterWhoTakesIt()
    {
        await ResetFarmAsync();
        FakeTimeProvider clock = new(s_start);
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        TestAccounts.UseFakeVerifiers(_database.Options);
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
            // One parsnip, or two on a rainy harvest (docs/adr/ADR-027 D8).
            dropped.Quantity.Should().BeInRange(1, 2);
            InventorySlotRow produce = await farmer.WaitForSlotAsync(s => s.ItemId == ItemCatalog.Parsnip, timeout.Token);
            produce.Quantity.Should().Be(dropped.Quantity);
            (await farmer.GetInventoryAsync(timeout.Token)).Slots.Should().Contain(s => s.ItemId == ItemCatalog.Parsnip && s.Quantity == dropped.Quantity);
            (await farmer.GetGroundItemsAsync(timeout.Token)).Items.Should().BeEmpty();

            // The first quest is complete and the guest is invited to register; tilling and watering finished their own steps of the tutorial chain on the way (docs/adr/ADR-036).
            QuestRow quest = await farmer.WaitForQuestAsync(q => q.QuestId == QuestCatalog.FirstHarvest && q.State == QuestState.Completed, timeout.Token);
            quest.Progress.Should().Equal(1, 1);
            await farmer.WaitForPromptUnlockedAsync(timeout.Token);
            QuestRow[] log = [.. (await farmer.GetQuestsAsync(timeout.Token)).Quests];
            log.Should().HaveCount(QuestCatalog.All.Count);
            log.Where(q => q.State == QuestState.Completed).Select(q => q.QuestId).Should().BeEquivalentTo([QuestCatalog.FirstHarvest, QuestCatalog.TillSoil, QuestCatalog.WaterCrop]);

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
        TestAccounts.UseFakeVerifiers(_database.Options);
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
        TestAccounts.UseFakeVerifiers(_database.Options);
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
    public async Task Rain_WateringSoilThatTheRainAlreadyWetIsRefused_AndTheCanIsKept()
    {
        await ResetFarmAsync();
        GameCalendar calendar = GameCalendar.Default;
        int needed = CropCatalog.All[0].TotalGrowthDays;

        // A rainy spring-like day, so the parsnip can be planted on it.
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

            clock.Advance(TimeSpan.FromTicks(s_gameDay.Ticks * (rainy - 1)));
            (await farmer.UseAsync(0, x, y, timeout.Token)).Action.Should().Be(FarmAction.Tilled);
            (await farmer.UseAsync(2, x, y, timeout.Token)).Action.Should().Be(FarmAction.Planted);

            // The rain has wet the soil today already: the can would add nothing, so it is refused before any water or energy is spent.
            (await farmer.UseAsync(1, x, y, timeout.Token)).Status.Should().Be(FarmStatus.AlreadyWet);
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
        TestAccounts.UseFakeVerifiers(_database.Options);
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
        TestAccounts.UseFakeVerifiers(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, clock, development: true);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(90));
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using FarmClient farmer = await FarmClient.ConnectAsync(port, timeout.Token);
            JoinMapResult join = await farmer.JoinAsync(timeout.Token);
            (int x, int y) = TileInReachOf(join);
            // On a rainy day the can is refused: the rain has watered the soil already, and it counts toward the growth as a hand watering would (docs/adr/ADR-027 D2).
            async Task WaterAsync()
            {
                // The dev clock moves the server's time, not the test's clock: the server's own time tells the day.
                long serverNowMs = (await farmer.GetFarmAsync(timeout.Token)).ServerNowMs;
                var watering = await farmer.UseAsync(1, x, y, timeout.Token);
                if (WeatherRules.WatersOn(GameCalendar.Default, GameCalendar.Default.DayNumberAt(serverNowMs)))
                {
                    watering.Status.Should().Be(FarmStatus.AlreadyWet, "the rain watered it today");
                }
                else
                {
                    watering.Action.Should().Be(FarmAction.Watered, "status " + watering.Status);
                }
            }

            CropDefinition berry = CropCatalog.All.Single(c => c.Id == CropCatalog.Strawberry);
            (await farmer.GrantAsync(ItemCatalog.StrawberrySeeds, 1, timeout.Token)).Should().BeTrue();
            int seeds = (await farmer.GetInventoryAsync(timeout.Token)).Slots.Single(s => s.ItemId == ItemCatalog.StrawberrySeeds).Slot;
            seeds.Should().Be(8, "the first free hotbar slot; the slot is empty again once the seed is planted, so a bare hand harvests from it");

            // Spring: a strawberry seed is planted and watered every day until it bears.
            (await farmer.UseAsync(0, x, y, timeout.Token)).Action.Should().Be(FarmAction.Tilled);
            (await farmer.UseAsync(seeds, x, y, timeout.Token)).Action.Should().Be(FarmAction.Planted);
            for (int day = 0; day < berry.TotalGrowthDays; day++)
            {
                await WaterAsync();
                (await farmer.AdvanceClockAsync(1, timeout.Token)).Ok.Should().BeTrue();
            }

            // First harvest: the berries drop, the plant stays, back at its regrow stage. A rainy harvest adds one berry (docs/adr/ADR-027 D8).
            long harvestNowMs = (await farmer.GetFarmAsync(timeout.Token)).ServerNowMs;
            (await farmer.UseAsync(seeds, x, y, timeout.Token)).Action.Should().Be(FarmAction.Harvested);
            int bonus = await BonusHarvestAsync(x, y, harvestNowMs, timeout.Token);
            (await farmer.WaitForGroundItemAsync(i => i.ItemId == ItemCatalog.Strawberry, timeout.Token)).Quantity.Should().Be(berry.HarvestQuantity + bonus);
            FarmTileRow standing = (await farmer.GetFarmAsync(timeout.Token)).Tiles.Single(t => t.X == x && t.Y == y);
            standing.CropId.Should().Be(CropCatalog.Strawberry, "the plant is still there");
            standing.GrowthDays.Should().Be(berry.GrowthDaysAfterHarvest);
            (await farmer.UseAsync(seeds, x, y, timeout.Token)).Status.Should().Be(FarmStatus.NotMature, "it has to be watered again first");

            // Regrow days of watering, then the second harvest, with no new seed.
            for (int day = 0; day < berry.RegrowDays; day++)
            {
                (await farmer.UseAsync(seeds, x, y, timeout.Token)).Status.Should().Be(FarmStatus.NotMature, $"{day} of {berry.RegrowDays} regrow days");

                await WaterAsync();
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
    public async Task Harvest_WithACompletelyFullInventory_LosesNothing_TheCropLiesOnTheGroundAndTheSoilIsFree()
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
            (await farmer.UseAsync(0, x, y, timeout.Token)).Action.Should().Be(FarmAction.Tilled);
            (await farmer.UseAsync(2, x, y, timeout.Token)).Action.Should().Be(FarmAction.Planted);
            for (int day = 1; day <= CropCatalog.All[0].TotalGrowthDays; day++)
            {
                (await farmer.UseAsync(1, x, y, timeout.Token)).Action.Should().Be(FarmAction.Watered);
                (await farmer.AdvanceClockAsync(1, timeout.Token)).Ok.Should().BeTrue();
            }

            // Every slot is taken by a full stack that the parsnip cannot join (the dev grant is rate limited, so the stacks are added on the server).
            Guid player;
            await using (NosumDbContext lookup = new(_database.Options))
            {
                player = (await lookup.Players.OrderByDescending(p => p.CreatedAt).FirstAsync(timeout.Token)).Id;
            }

            IInventoryService inventory = InstanceManager.Instance.GetExistingInstance<IInventoryService>()!;
            _ = await inventory.AddAsync(player, ItemCatalog.Wood, 99 * 28, "test.fill", timeout.Token);
            (await farmer.GetInventoryAsync(timeout.Token)).Slots.Should().HaveCount(36);

            UseItemResult harvested = await farmer.UseAsync(0, x, y, timeout.Token);

            harvested.Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Harvested });
            _ = await farmer.WaitForTileAsync(x, y, t => t.CropId == 0 && !t.Removed, timeout.Token);
            GroundItemRow dropped = await farmer.WaitForGroundItemAsync(g => g.ItemId == ItemCatalog.Parsnip, timeout.Token);
            // One parsnip, or two on a rainy harvest (docs/adr/ADR-027 D8).
            dropped.Quantity.Should().BeInRange(1, 2);
            (await farmer.GetInventoryAsync(timeout.Token)).Slots.Should().NotContain(s => s.ItemId == ItemCatalog.Parsnip);
            (await farmer.GetGroundItemsAsync(timeout.Token)).Items.Should().ContainSingle(g => g.ItemId == ItemCatalog.Parsnip, "the crop waits on the ground for the player");
            await using NosumDbContext context = new(_database.Options);
            (await context.GroundItems.CountAsync(g => g.ItemId == ItemCatalog.Parsnip)).Should().Be(1, "the drop is stored, so a restart does not lose it");
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
        TestAccounts.UseFakeVerifiers(_database.Options);
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
        TestAccounts.UseFakeVerifiers(_database.Options);
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

    // The rain bonus roll is seeded by the farm's instance key, which contains the owner's id: every run creates a new player,
    // so the bonus for a tile and day differs from run to run. The expected bonus is the rule's answer for this owner's farm.
    private async Task<int> BonusHarvestAsync(int x, int y, long nowMs, CancellationToken ct)
    {
        Guid player;
        await using (NosumDbContext lookup = new(_database.Options))
        {
            player = (await lookup.Players.OrderByDescending(p => p.CreatedAt).FirstAsync(ct)).Id;
        }

        MapInstance farm = new(Program.FarmMapId, player);
        return FarmRules.IsBonusHarvest(farm.Key, x, y, nowMs, GameCalendar.Default, farm.IsOutdoors) ? 1 : 0;
    }

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
        TestAccounts.UseFakeVerifiers(_database.Options);
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
        TestAccounts.UseFakeVerifiers(_database.Options);
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
        TestAccounts.UseFakeVerifiers(_database.Options);
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
        TestAccounts.UseFakeVerifiers(_database.Options);
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
        TestAccounts.UseFakeVerifiers(_database.Options);
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
}

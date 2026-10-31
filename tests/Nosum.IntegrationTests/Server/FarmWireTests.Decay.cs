// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.SDK.Options;
using Nalix.SDK.Transport.Extensions;
using Nalix.SDK.Transport;
using Nosum.Abstractions.Calendar;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Farm;
using Nosum.Contracts.GroundItem.C2S;
using Nosum.Contracts.GroundItem.S2C;
using Nosum.Contracts.GroundItem;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Quest.C2S;
using Nosum.Contracts.Quest.S2C;
using Nosum.Contracts.Quest;
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
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;
using Xunit;

namespace Nosum.IntegrationTests.Server;

// The dry-day rules over the wire (docs/adr/ADR-021 D7, amended 2026-10-31): a crop with two dry game days behind it withers and refuses water;
// tilled soil with three dry game days behind it reverts to grass, and the server answers the refused use with an explicit status.
public sealed partial class FarmWireTests
{
    [Fact]
    public async Task Decay_TwoDryDaysAfterPlanting_WitherTheCrop_AndWaterIsRefused()
    {
        await ResetFarmAsync();
        GameCalendar calendar = GameCalendar.Default;
        FakeTimeProvider clock = new(DryStart(calendar, 4));
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

            // Planted today; the next two game days pass without water, so they are the two dry days behind it.
            clock.Advance(s_gameDay);
            clock.Advance(s_gameDay);
            clock.Advance(s_gameDay);

            FarmSnapshot farm = await farmer.GetFarmAsync(timeout.Token);
            FarmTileRow row = farm.Tiles.Single(t => t.X == x && t.Y == y);
            FarmRules.IsWithered(ToTile(row), farm.ServerNowMs, calendar, outdoors: true).Should().BeTrue("two dry game days in a row withered it");

            // The watering can is refused with its own status, and the dead crop keeps its state: nothing is watered.
            UseItemResult watering = await farmer.UseAsync(1, x, y, timeout.Token);
            watering.Status.Should().Be(FarmStatus.Withered, "water does not revive a dead crop");
            ToTile((await farmer.GetFarmAsync(timeout.Token)).Tiles.Single(t => t.X == x && t.Y == y)).WateredDay.Should().Be(row.WateredDay);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Decay_TilledSoilLeftUnplanted_RevertsToGrassAfterThreeDryDays()
    {
        await ResetFarmAsync();
        GameCalendar calendar = GameCalendar.Default;
        FakeTimeProvider clock = new(DryStart(calendar, 4));
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

            // Two dry days: still tilled. Three dry days in a row: the soil is grass again, and no client is told a tile that is gone.
            clock.Advance(s_gameDay);
            clock.Advance(s_gameDay);
            (await farmer.GetFarmAsync(timeout.Token)).Tiles.Any(t => t.X == x && t.Y == y).Should().BeTrue("two dry days are not enough");

            clock.Advance(s_gameDay);
            clock.Advance(s_gameDay);
            (await farmer.GetFarmAsync(timeout.Token)).Tiles.Any(t => t.X == x && t.Y == y).Should().BeFalse("three dry game days reverted the soil");
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Decay_WateredEveryDay_TheCropNeverWithers()
    {
        await ResetFarmAsync();
        GameCalendar calendar = GameCalendar.Default;
        FakeTimeProvider clock = new(DryStart(calendar, 4));
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

            for (int day = 0; day < 3; day++)
            {
                clock.Advance(s_gameDay);
                (await farmer.UseAsync(1, x, y, timeout.Token)).Action.Should().Be(FarmAction.Watered, $"day {day}: watered, so it is not dry");
            }
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }

    // The first start, from the usual one, whose day and the next `days` game days have no rain and stay in the spring the crops grow in: the
    // dry days the tests count then come from the server's own rule alone, not from the weather.
    private static DateTimeOffset DryStart(GameCalendar calendar, int days)
    {
        for (int k = 0; k < 400; k++)
        {
            DateTimeOffset start = s_start + TimeSpan.FromMilliseconds(k * (double)calendar.DayLengthMs);
            int first = calendar.DayNumberAt(start.ToUnixTimeMilliseconds());
            if (Enumerable.Range(first, days + 1).All(d => !WeatherRules.WatersOn(calendar, d) && calendar.SeasonAt(calendar.DayStartMs(d)) == Season.Spring))
            {
                return start;
            }
        }

        throw new InvalidOperationException("the calendar has no dry spring days from the usual start");
    }
}

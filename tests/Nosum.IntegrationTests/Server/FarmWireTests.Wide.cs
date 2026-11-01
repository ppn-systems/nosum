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

// The wide uses over the wire: a hoe on a line (docs/adr/ADR-026 D1, ADR-088) and a scythe on a line (docs/adr/ADR-094 D1). Split from
// FarmWireTests.cs to keep it under the source-size ceiling.
public sealed partial class FarmWireTests
{
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
            // each of them was charged its own energy (docs/adr/ADR-088), so the count is what the client shows as the energy spent.
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
    public async Task WideUse_TheBasicScytheWorksALineOfThree_ALineOfFiveIsRefused()
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
            InventorySlotRow scythe = (await farmer.GetInventoryAsync(timeout.Token)).Slots.Single(s => s.ItemId == ItemCatalog.Scythe);

            // The basic scythe admits a line of three (docs/adr/ADR-094 D1). Nothing is tilled here, so nothing is worked, but the line is not refused.
            UseItemResult line = await farmer.UseAreaAsync(scythe.Slot, x, y, 3, timeout.Token);
            line.Status.Should().Be(FarmStatus.NothingToDo);
            line.Worked.Should().Be(0);

            // Five is past the basic scythe's line.
            UseItemResult tooWide = await farmer.UseAreaAsync(scythe.Slot, x, y, 5, timeout.Token);
            tooWide.Should().BeEquivalentTo(new { Status = FarmStatus.InvalidArea, Worked = (byte)0 });
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }
}

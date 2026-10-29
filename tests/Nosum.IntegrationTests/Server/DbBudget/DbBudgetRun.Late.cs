// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Calendar;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Admin;
using Nosum.Contracts.Admin.C2S;
using Nosum.Contracts.Admin.S2C;
using Nosum.Contracts.Chat;
using Nosum.Contracts.Chat.C2S;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Mine;
using Nosum.Contracts.Mine.C2S;
using Nosum.Contracts.Mine.S2C;
using Nosum.Contracts.Shop;
using Nosum.Contracts.Shop.C2S;
using Nosum.Contracts.Shop.S2C;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;

namespace Nosum.IntegrationTests.Server.DbBudget;

public sealed partial class DbBudgetRun
{
    // The admin tools (ADR-031): an admin account moderates a player who chatted and was reported.
    private async Task AdminAsync()
    {
        string adminNick = this.NextNickname();
        string adminToken = await _rig.RegisteredAccountAsync(adminNick, "budget." + adminNick + "@gmail.com");
        Guid adminId;
        await using (BudgetPlayer first = await _rig.ConnectAsync())
        {
            adminId = (await first.ResumeAsync(adminToken)).PlayerId;
        }

        await using (NosumDbContext context = new(_rig.Options))
        {
            _ = await context.Players.Where(p => p.Id == adminId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Role, PlayerRole.Admin));
        }

        await using BudgetPlayer admin = await _rig.ConnectAsync();
        _ = await admin.ResumeAsync(adminToken);

        (double ax, double ay) = TileCentre(5, 54);
        (double bx, double by) = TileCentre(6, 54);
        await using BudgetPlayer talker = await this.FreshAsync(GameMaps.Town, ax, ay, registered: true, 0);
        await using BudgetPlayer reporter = await this.FreshAsync(GameMaps.Town, bx, by, registered: true, 0);
        using (SendChat line = new() { Channel = ChatChannel.Map, Text = "a line to report" })
        {
            _ = await talker.RequestAsync<Nosum.Contracts.Chat.S2C.ChatResult>(line);
        }

        using (ReportChat report = new() { Nickname = talker.Nickname })
        {
            _ = await reporter.RequestAsync<Nosum.Contracts.Chat.S2C.ChatResult>(report);
        }

        AdminReportList reports = await _rig.ProbeAsync<AdminReportList>(admin, "Admin", AdminOpCode.ListReports, "ListReports", "one report", () => new AdminListReports());
        if (reports.Reports.Count > 0)
        {
            Guid id = reports.Reports[0].ReportId;
            _ = await _rig.ProbeAsync<AdminReportDetail>(admin, "Admin", AdminOpCode.ReadReport, "ReadReport", "one report with its chat lines", () => new AdminReadReport { ReportId = id });
        }

        _ = await _rig.ProbeAsync<AdminResult>(admin, "Admin", AdminOpCode.GrantItem, "GrantItem", "items and coins to a player", () => new AdminGrantItem { Nickname = talker.Nickname, ItemId = ItemCatalog.Parsnip, Quantity = 1, Coins = 10 });
        _ = await _rig.ProbeAsync<AdminResult>(admin, "Admin", AdminOpCode.Mute, "Mute", "one hour", () => new AdminMute { Nickname = talker.Nickname, Hours = 1 });
        _ = await _rig.ProbeAsync<AdminResult>(admin, "Admin", AdminOpCode.Kick, "Kick", "a connected player", () => new AdminKick { Nickname = talker.Nickname });
        _ = await _rig.ProbeAsync<AdminResult>(admin, "Admin", AdminOpCode.Ban, "Ban", "a player", () => new AdminBan { Nickname = talker.Nickname });
        _ = await _rig.ProbeAsync<AdminResult>(admin, "Admin", AdminOpCode.Unban, "Unban", "a banned player", () => new AdminUnban { Nickname = talker.Nickname });
    }

    // Changing and recovering a password (ADR-032). The reset code goes out by e-mail, so the confirm is measured with a wrong code (a refusal).
    private async Task PasswordAsync()
    {
        string nick = this.NextNickname();
        string email = "budget." + nick + "@gmail.com";
        string token = await _rig.RegisteredAccountAsync(nick, email);
        await using BudgetPlayer p = await _rig.ConnectAsync();
        _ = await p.ResumeAsync(token);
        _ = await _rig.ProbeAsync<PasswordResult>(p, "Account", AccountOpCode.ChangePassword, "ChangePassword", "right current password", () => new ChangePassword { CurrentPassword = "correct horse battery", NewPassword = "another horse battery" });
    }

    // The blacksmith upgrades a tool in two game days.
    private async Task BlacksmithAsync()
    {
        (double x, double y) = TileCentre(MineWorld.BlacksmithCounter.X, MineWorld.BlacksmithCounter.Y);
        await using BudgetPlayer p = await this.FreshAsync(GameMaps.Town, x, y, 100_000, (ItemCatalog.CopperBar, 5));
        _ = await _rig.ProbeAsync<BlacksmithState>(p, "Blacksmith", ShopOpCode.GetBlacksmith, "GetBlacksmith", "nothing in the forge", () => new GetBlacksmith());
        _ = await _rig.ProbeAsync<BlacksmithState>(p, "Blacksmith", ShopOpCode.UpgradeTool, "UpgradeTool", "hoe to copper", () => new UpgradeTool { ItemId = ItemCatalog.Hoe });
        _ = BudgetRig.Service<IDevClockService>().AdvanceDays(3);
        await _rig.Counter.SettleAsync(TimeSpan.FromMilliseconds(300), _rig.Timeout);
        _ = await _rig.ProbeAsync<BlacksmithState>(p, "Blacksmith", ShopOpCode.CollectTool, "CollectTool", "a finished upgrade", () => new CollectTool());
    }

    // The mine elevator in the entrance cave; deeper stops are unlocked by playing, so the ride measured is the one to the entrance.
    private async Task MineAsync()
    {
        (double x, double y) = TileCentre(MineWorld.CaveElevator.X, MineWorld.CaveElevator.Y + 1);
        await using BudgetPlayer p = await this.FreshAsync(GameMaps.Farm, 1032, 264, 0);
        await BudgetRig.TravelAsync(p.AccountId, MapInstance.Shared(GameMaps.Mine));
        BudgetRig.Stand(p.AccountId, x, y);
        await _rig.Counter.SettleAsync(TimeSpan.FromMilliseconds(300), _rig.Timeout);
        _ = await _rig.ProbeAsync<MineState>(p, "Mine", MineOpCode.GetMine, "GetMine", "in the entrance cave", () => new GetMine());
        _ = await _rig.ProbeAsync<MineState>(p, "Mine", MineOpCode.RideElevator, "RideElevator", "to the entrance, already there", () => new RideElevator { Floor = MineRules.EntranceFloor });
    }

    // Watering a block of tiles with one swing.
    private async Task AreaAsync()
    {
        await using BudgetPlayer p = await this.FreshAsync(GameMaps.Farm, 1032, 264, 0);
        (int x, int y) = TileInReachOf(p.Feet.X, p.Feet.Y);
        _ = await this.UseAsync(p, "till for the area swing", 0, x, y, measure: false);
        await Task.Delay(s_swingGap, _rig.Timeout);
        _ = await _rig.ProbeAsync<UseItemResult>(p, "Farm", FarmOpCode.UseItemArea, "UseItemArea", "watering can over a block of tiles", () => new UseItemArea { Slot = 1, TileX = x, TileY = y, Tiles = 3 });
    }
}

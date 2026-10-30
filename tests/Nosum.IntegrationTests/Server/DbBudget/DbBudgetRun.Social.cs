// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Threading.Tasks;
using Nosum.Contracts.Chat;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.C2S;
using Nosum.Contracts.Combat.S2C;
using Nosum.Contracts.Chat.C2S;
using Nosum.Contracts.Chat.S2C;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Coop.C2S;
using Nosum.Contracts.Coop.S2C;
using Nosum.Contracts.Friends;
using Nosum.Contracts.Friends.C2S;
using Nosum.Contracts.Friends.S2C;
using Nosum.Contracts.Journal;
using Nosum.Contracts.Journal.C2S;
using Nosum.Contracts.Journal.S2C;
using Nosum.Contracts.Look;
using Nosum.Contracts.Look.C2S;
using Nosum.Contracts.Look.S2C;
using Nosum.Contracts.Trade;
using Nosum.Contracts.Trade.C2S;
using Nosum.Contracts.Trade.S2C;
using Nosum.Contracts.Visit;
using Nosum.Contracts.Visit.C2S;
using Nosum.Contracts.Visit.S2C;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;

namespace Nosum.IntegrationTests.Server.DbBudget;

public sealed partial class DbBudgetRun
{
    // Two players side by side in the Town: chat, blocking, friends, visiting, trading, a co-op and their looks.
    private async Task SocialAsync()
    {
        (double ax, double ay) = TileCentre(5, 54);
        (double bx, double by) = TileCentre(6, 54);
        await using BudgetPlayer a = await this.FreshAsync(GameMaps.Town, ax, ay, registered: true, 1_000, (ItemCatalog.Parsnip, 20), (ItemCatalog.ShirtRed, 1));
        await using BudgetPlayer b = await this.FreshAsync(GameMaps.Town, bx, by, registered: true, 1_000, (ItemCatalog.Parsnip, 20));

        // Chat.
        _ = await _rig.ProbeAsync<ChatResult>(a, "Chat", ChatOpCode.SendChat, "SendChat", "map channel", () => new SendChat { Channel = ChatChannel.Map, Text = "hello" });
        _ = await _rig.ProbeAsync<ChatResult>(a, "Chat", ChatOpCode.SendChat, "SendChat", "whisper by nickname", () => new SendChat { Channel = ChatChannel.Whisper, Target = b.Nickname, Text = "psst" });
        _ = await _rig.ProbeAsync<ChatResult>(a, "Chat", ChatOpCode.SendChat, "SendChat", "local channel", () => new SendChat { Channel = ChatChannel.Local, Text = "anyone?" });
        _ = await _rig.ProbeAsync<ChatResult>(a, "Chat", ChatOpCode.ReportChat, "ReportChat", "report a player", () => new ReportChat { Nickname = b.Nickname });
        _ = await _rig.ProbeAsync<ChatResult>(a, "Chat", ChatOpCode.BlockPlayer, "BlockPlayer", "block by nickname", () => new BlockPlayer { Nickname = b.Nickname });
        _ = await _rig.ProbeAsync<BlockedList>(a, "Chat", ChatOpCode.GetBlocked, "GetBlocked", "one block", () => new GetBlocked());
        _ = await _rig.ProbeAsync<ChatResult>(b, "Chat", ChatOpCode.SendChat, "SendChat", "map channel, one listener has blocked the sender", () => new SendChat { Channel = ChatChannel.Map, Text = "hi" });
        _ = await _rig.ProbeAsync<ChatResult>(a, "Chat", ChatOpCode.UnblockPlayer, "UnblockPlayer", "unblock", () => new UnblockPlayer { Nickname = b.Nickname });

        // Looks.
        _ = await _rig.ProbeAsync<LookResult>(a, "Look", LookOpCode.SetAppearance, "SetAppearance", "skin, hair, dye", () => new SetAppearance { SkinId = 1, HairStyleId = 1, HairDyeId = 1 });
        int shirt = await this.SlotOfAsync(a, ItemCatalog.ShirtRed);
        _ = await _rig.ProbeAsync<LookResult>(a, "Look", LookOpCode.EquipItem, "EquipItem", "wear a shirt", () => new EquipItem { Slot = (byte)shirt, ItemId = ItemCatalog.ShirtRed });
        _ = await _rig.ProbeAsync<LookResult>(a, "Look", LookOpCode.UnequipItem, "UnequipItem", "take the shirt off", () => new UnequipItem { WearSlot = (byte)WearSlot.Shirt });

        // Friends.
        _ = await _rig.ProbeAsync<FriendResult>(a, "Friends", FriendsOpCode.RequestFriend, "RequestFriend", "by nickname", () => new RequestFriend { Nickname = b.Nickname });
        _ = await _rig.ProbeAsync<FriendsSnapshot>(b, "Friends", FriendsOpCode.GetFriends, "GetFriends", "one request waiting", () => new GetFriends());
        _ = await _rig.ProbeAsync<FriendResult>(b, "Friends", FriendsOpCode.RespondFriend, "RespondFriend", "accept", () => new RespondFriend { PlayerId = a.AccountId, Accept = true });
        _ = await _rig.ProbeAsync<FriendsSnapshot>(a, "Friends", FriendsOpCode.GetFriends, "GetFriends", "one friend", () => new GetFriends());
        _ = await _rig.ProbeAsync<FriendResult>(a, "Friends", FriendsOpCode.SetFriendsVisit, "SetFriendsVisit", "allow friends to visit", () => new SetFriendsVisit { Allowed = true });

        // Visiting: B walks into A's farm as A's friend; A then manages the list.
        _ = await _rig.ProbeAsync<VisitResult>(b, "Visit", VisitOpCode.VisitPlayer, "VisitPlayer", "friend visits the farm", () => new VisitPlayer { Nickname = a.Nickname });
        _ = await _rig.ProbeAsync<VisitorsSnapshot>(a, "Visit", VisitOpCode.GetVisitors, "GetVisitors", "one visitor on the farm", () => new GetVisitors());
        _ = await _rig.ProbeAsync<GuestbookSnapshot>(a, "Visit", VisitOpCode.GetGuestbook, "GetGuestbook", "read the guestbook", () => new GetGuestbook());
        _ = await _rig.ProbeAsync<BestiarySnapshot>(a, "Bestiary", CombatOpCode.GetBestiary, "GetBestiary", "read the hunting log", () => new GetBestiary());
        _ = await _rig.ProbeAsync<EquipBestiaryTitleResult>(a, "Bestiary", CombatOpCode.EquipBestiaryTitle, "EquipBestiaryTitle", "equip a title not earned yet", () => new EquipBestiaryTitle { Species = 4 });
        _ = await _rig.ProbeAsync<JournalReply>(a, "Journal", JournalOpCode.GetJournal, "GetJournal", "read the journal", () => new GetJournal());
        _ = await _rig.ProbeAsync<VisitResult>(a, "Visit", VisitOpCode.KickVisitor, "KickVisitor", "send the visitor away", () => new KickVisitor { PlayerId = b.AccountId });
        _ = await _rig.ProbeAsync<VisitResult>(a, "Visit", VisitOpCode.AddVisitor, "AddVisitor", "allow-list by nickname", () => new AddVisitor { Nickname = b.Nickname });
        _ = await _rig.ProbeAsync<VisitResult>(a, "Visit", VisitOpCode.RemoveVisitor, "RemoveVisitor", "take off the allow-list", () => new RemoveVisitor { PlayerId = b.AccountId });
        _ = await _rig.ProbeAsync<VisitResult>(a, "Visit", VisitOpCode.SetCoopVisits, "SetCoopVisits", "co-op members may visit", () => new SetCoopVisits { Allowed = true });
        _ = await _rig.ProbeAsync<FriendResult>(a, "Friends", FriendsOpCode.RemoveFriend, "RemoveFriend", "unfriend", () => new RemoveFriend { PlayerId = b.AccountId });

        // Both back in the Town (a kicked visitor lands there; the owner is on their farm).
        await this.TradeAsync(a, b);
        await this.CoopAsync(a, b);
    }

    private async Task TradeAsync(BudgetPlayer a, BudgetPlayer b)
    {
        _ = await _rig.ProbeAsync<TradeResult>(a, "Trade", TradeOpCode.TradeRequest, "TradeRequest", "invite a neighbour", () => new TradeRequest { TargetPlayerId = b.MapPlayerId });
        _ = await _rig.ProbeAsync<TradeResult>(b, "Trade", TradeOpCode.TradeRespond, "TradeRespond", "accept", () => new TradeRespond { Accept = true });
        _ = await _rig.ProbeAsync<TradeResult>(a, "Trade", TradeOpCode.TradeSetOffer, "TradeSetOffer", "items and coins", () => new TradeSetOffer { Items = [new TradeItemRow { ItemId = ItemCatalog.Parsnip, Quantity = 2 }], Coins = 10 });
        _ = await _rig.ProbeAsync<TradeResult>(b, "Trade", TradeOpCode.TradeSetOffer, "TradeSetOffer", "items", () => new TradeSetOffer { Items = [new TradeItemRow { ItemId = ItemCatalog.Parsnip, Quantity = 1 }], Coins = 0 });
        _ = await _rig.ProbeAsync<TradeResult>(a, "Trade", TradeOpCode.TradeConfirm, "TradeConfirm", "first side confirms", () => new TradeConfirm());
        _ = await _rig.ProbeAsync<TradeResult>(b, "Trade", TradeOpCode.TradeConfirm, "TradeConfirm", "second side confirms: the swap", () => new TradeConfirm());

        _ = await _rig.ProbeAsync<TradeResult>(a, "Trade", TradeOpCode.TradeRequest, "TradeRequest", "second trade", () => new TradeRequest { TargetPlayerId = b.MapPlayerId });
        _ = await _rig.ProbeAsync<TradeResult>(b, "Trade", TradeOpCode.TradeRespond, "TradeRespond", "accept", () => new TradeRespond { Accept = true });
        _ = await _rig.ProbeAsync<TradeResult>(a, "Trade", TradeOpCode.TradeCancel, "TradeCancel", "cancel an open trade", () => new TradeCancel());
    }

    private async Task CoopAsync(BudgetPlayer a, BudgetPlayer b)
    {
        _ = await _rig.ProbeAsync<CoopResult>(a, "Coop", CoopOpCode.CreateCoop, "CreateCoop", "found a co-op", () => new CreateCoop { Name = "Budget Farmers" });
        _ = await _rig.ProbeAsync<CoopResult>(a, "Coop", CoopOpCode.InvitePlayer, "InvitePlayer", "invite a neighbour", () => new InvitePlayer { TargetPlayerId = b.MapPlayerId });
        CoopSnapshot waiting = await _rig.ProbeAsync<CoopSnapshot>(b, "Coop", CoopOpCode.GetCoop, "GetCoop", "an invitation waiting", () => new GetCoop());
        if (waiting.Invites.Count > 0)
        {
            Guid invite = waiting.Invites[0].InviteId;
            _ = await _rig.ProbeAsync<CoopResult>(b, "Coop", CoopOpCode.RespondInvite, "RespondInvite", "accept", () => new RespondInvite { InviteId = invite, Accept = true });
        }

        _ = await _rig.ProbeAsync<CoopResult>(a, "Coop", CoopOpCode.DepositItem, "DepositItem", "into the warehouse", () => new DepositItem { ItemId = ItemCatalog.Parsnip, Quantity = 3 });
        _ = await _rig.ProbeAsync<CoopResult>(a, "Coop", CoopOpCode.WithdrawItem, "WithdrawItem", "out of the warehouse", () => new WithdrawItem { ItemId = ItemCatalog.Parsnip, Quantity = 1 });
        _ = await _rig.ProbeAsync<CoopSnapshot>(a, "Coop", CoopOpCode.GetCoop, "GetCoop", "two members, stock and log", () => new GetCoop());
        _ = await _rig.ProbeAsync<CoopResult>(b, "Coop", CoopOpCode.LeaveCoop, "LeaveCoop", "a member leaves", () => new LeaveCoop());
        _ = await _rig.ProbeAsync<CoopResult>(a, "Coop", CoopOpCode.InvitePlayer, "InvitePlayer", "invite again", () => new InvitePlayer { TargetPlayerId = b.MapPlayerId });
        CoopSnapshot again = await this.CoopSnapshotAsync(b);
        if (again.Invites.Count > 0)
        {
            Guid invite = again.Invites[0].InviteId;
            _ = await _rig.ProbeAsync<CoopResult>(b, "Coop", CoopOpCode.RespondInvite, "RespondInvite", "accept again", () => new RespondInvite { InviteId = invite, Accept = true });
        }

        CoopSnapshot full = await this.CoopSnapshotAsync(a);
        Guid member = full.Members.First(m => !m.IsMe).MemberId;
        _ = await _rig.ProbeAsync<CoopResult>(a, "Coop", CoopOpCode.KickMember, "KickMember", "owner removes a member", () => new KickMember { MemberId = member });
    }

    private async Task<CoopSnapshot> CoopSnapshotAsync(BudgetPlayer p)
    {
        using GetCoop request = new();
        return await p.RequestAsync<CoopSnapshot>(request);
    }
}

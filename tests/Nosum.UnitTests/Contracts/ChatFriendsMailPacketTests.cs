// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Nosum.Contracts.Chat;
using Nosum.Contracts.Chat.C2S;
using Nosum.Contracts.Chat.S2C;
using Nosum.Contracts.Friends;
using Nosum.Contracts.Friends.C2S;
using Nosum.Contracts.Friends.S2C;
using Nosum.Contracts.Mail;
using Nosum.Contracts.Mail.C2S;
using Nosum.Contracts.Mail.S2C;
using Nosum.Contracts.Trade;
using Xunit;

namespace Nosum.UnitTests.Contracts;

public sealed class ChatFriendsMailPacketTests
{
    [Fact]
    public void ChatOpCode_Values_AreFrozen()
    {
        // Append-only (docs/adr/ADR-029 D6): these numbers never change and are never reused.
        ChatOpCode.SendChat.Should().Be(0x9100);
        ChatOpCode.BlockPlayer.Should().Be(0x9101);
        ChatOpCode.UnblockPlayer.Should().Be(0x9102);
        ChatOpCode.GetBlocked.Should().Be(0x9103);
        ChatOpCode.ReportChat.Should().Be(0x9104);
        ChatOpCode.ChatResult.Should().Be(0x9140);
        ChatOpCode.ChatMessage.Should().Be(0x9141);
        ChatOpCode.BlockedList.Should().Be(0x9142);
    }

    [Fact]
    public void FriendsOpCode_Values_AreFrozen()
    {
        FriendsOpCode.GetFriends.Should().Be(0x9200);
        FriendsOpCode.RequestFriend.Should().Be(0x9201);
        FriendsOpCode.RespondFriend.Should().Be(0x9202);
        FriendsOpCode.RemoveFriend.Should().Be(0x9203);
        FriendsOpCode.SetFriendsVisit.Should().Be(0x9204);
        FriendsOpCode.FriendResult.Should().Be(0x9240);
        FriendsOpCode.FriendsSnapshot.Should().Be(0x9241);
        FriendsOpCode.FriendStatusChanged.Should().Be(0x9242);
    }

    [Fact]
    public void MailOpCode_Values_AreFrozen()
    {
        MailOpCode.GetMail.Should().Be(0x9300);
        MailOpCode.ReadMail.Should().Be(0x9301);
        MailOpCode.CollectMail.Should().Be(0x9302);
        MailOpCode.DeleteMail.Should().Be(0x9303);
        MailOpCode.MailResult.Should().Be(0x9340);
        MailOpCode.MailList.Should().Be(0x9341);
        MailOpCode.MailUnread.Should().Be(0x9342);
    }

    [Fact]
    public void OpCodes_StayInTheirSubRangesOfTheTradeBand_AndAreUniqueAcrossTheBand()
    {
        ushort[] chat = Values(typeof(ChatOpCode));
        ushort[] friends = Values(typeof(FriendsOpCode));
        ushort[] mail = Values(typeof(MailOpCode));
        ushort[] trade = Values(typeof(TradeOpCode));

        chat.Should().OnlyContain(v => v >= 0x9100 && v <= 0x91FF);
        friends.Should().OnlyContain(v => v >= 0x9200 && v <= 0x92FF);
        mail.Should().OnlyContain(v => v >= 0x9300 && v <= 0x93FF);
        trade.Should().OnlyContain(v => v >= 0x9000 && v <= 0x90FF);
        chat.Concat(friends).Concat(mail).Concat(trade).Should().OnlyHaveUniqueItems();
        chat.Where(v => v <= 0x913F).Should().HaveCount(5, "requests use the lower half");
        friends.Where(v => v <= 0x923F).Should().HaveCount(5);
        mail.Where(v => v <= 0x933F).Should().HaveCount(4);
    }

    [Fact]
    public void StaticOpCodes_OfEveryPacket_MatchTheirConstants()
    {
        SendChat.StaticOpCode.Should().Be(ChatOpCode.SendChat);
        BlockPlayer.StaticOpCode.Should().Be(ChatOpCode.BlockPlayer);
        UnblockPlayer.StaticOpCode.Should().Be(ChatOpCode.UnblockPlayer);
        GetBlocked.StaticOpCode.Should().Be(ChatOpCode.GetBlocked);
        ReportChat.StaticOpCode.Should().Be(ChatOpCode.ReportChat);
        ChatResult.StaticOpCode.Should().Be(ChatOpCode.ChatResult);
        ChatMessage.StaticOpCode.Should().Be(ChatOpCode.ChatMessage);
        BlockedList.StaticOpCode.Should().Be(ChatOpCode.BlockedList);
        GetFriends.StaticOpCode.Should().Be(FriendsOpCode.GetFriends);
        RequestFriend.StaticOpCode.Should().Be(FriendsOpCode.RequestFriend);
        RespondFriend.StaticOpCode.Should().Be(FriendsOpCode.RespondFriend);
        RemoveFriend.StaticOpCode.Should().Be(FriendsOpCode.RemoveFriend);
        SetFriendsVisit.StaticOpCode.Should().Be(FriendsOpCode.SetFriendsVisit);
        FriendResult.StaticOpCode.Should().Be(FriendsOpCode.FriendResult);
        FriendsSnapshot.StaticOpCode.Should().Be(FriendsOpCode.FriendsSnapshot);
        FriendStatusChanged.StaticOpCode.Should().Be(FriendsOpCode.FriendStatusChanged);
        GetMail.StaticOpCode.Should().Be(MailOpCode.GetMail);
        ReadMail.StaticOpCode.Should().Be(MailOpCode.ReadMail);
        CollectMail.StaticOpCode.Should().Be(MailOpCode.CollectMail);
        DeleteMail.StaticOpCode.Should().Be(MailOpCode.DeleteMail);
        MailResult.StaticOpCode.Should().Be(MailOpCode.MailResult);
        MailList.StaticOpCode.Should().Be(MailOpCode.MailList);
        MailUnread.StaticOpCode.Should().Be(MailOpCode.MailUnread);
    }

    [Fact]
    public void StatusAndChannelEnums_AreAppendOnly()
    {
        ((byte)ChatChannel.Local).Should().Be(0);
        ((byte)ChatChannel.Map).Should().Be(1);
        ((byte)ChatChannel.Coop).Should().Be(2);
        ((byte)ChatChannel.Whisper).Should().Be(3);

        ((byte)ChatStatus.Ok).Should().Be(0);
        ((byte)ChatStatus.Empty).Should().Be(1);
        ((byte)ChatStatus.NotOnMap).Should().Be(2);
        ((byte)ChatStatus.GuestLocalOnly).Should().Be(3);
        ((byte)ChatStatus.NotInCoop).Should().Be(4);
        ((byte)ChatStatus.TargetOffline).Should().Be(5);
        ((byte)ChatStatus.SelfWhisper).Should().Be(6);
        ((byte)ChatStatus.InvalidNickname).Should().Be(7);
        ((byte)ChatStatus.Blocked).Should().Be(8);
        ((byte)ChatStatus.RateLimited).Should().Be(9);
        ((byte)ChatStatus.UnknownPlayer).Should().Be(10);
        ((byte)ChatStatus.AlreadyBlocked).Should().Be(11);
        ((byte)ChatStatus.NotBlocked).Should().Be(12);
        ((byte)ChatStatus.BlockListFull).Should().Be(13);
        ((byte)ChatStatus.InvalidChannel).Should().Be(14);
        ((byte)ChatStatus.NothingToReport).Should().Be(15);
        ((byte)ChatStatus.SelfBlock).Should().Be(16);

        ((byte)FriendStatus.Ok).Should().Be(0);
        ((byte)FriendStatus.InvalidNickname).Should().Be(1);
        ((byte)FriendStatus.UnknownPlayer).Should().Be(2);
        ((byte)FriendStatus.Self).Should().Be(3);
        ((byte)FriendStatus.GuestNotAllowed).Should().Be(4);
        ((byte)FriendStatus.AlreadyFriends).Should().Be(5);
        ((byte)FriendStatus.AlreadyRequested).Should().Be(6);
        ((byte)FriendStatus.ListFull).Should().Be(7);
        ((byte)FriendStatus.NoRequest).Should().Be(8);
        ((byte)FriendStatus.NotFriends).Should().Be(9);
        ((byte)FriendStatus.PendingFull).Should().Be(10);
        ((byte)FriendStatus.Conflict).Should().Be(11);

        ((byte)MailStatus.Ok).Should().Be(0);
        ((byte)MailStatus.NotFound).Should().Be(1);
        ((byte)MailStatus.NothingToCollect).Should().Be(2);
        ((byte)MailStatus.AlreadyCollected).Should().Be(3);
        ((byte)MailStatus.Expired).Should().Be(4);
        ((byte)MailStatus.NoSpace).Should().Be(5);
        ((byte)MailStatus.TooRich).Should().Be(6);
        ((byte)MailStatus.HasAttachments).Should().Be(7);
        ((byte)MailStatus.Conflict).Should().Be(8);
        ((byte)MailStatus.Failed).Should().Be(9);
    }

    [Fact]
    public void ChatPackets_RoundTrip_WithVietnameseAndEmojiTextIntact()
    {
        using SendChat send = new() { Channel = ChatChannel.Whisper, Target = "Lan Anh", Text = "Chào bạn 😀 — hẹn gặp ở chợ" };
        using ChatMessage message = new() { Channel = ChatChannel.Coop, FromNickname = "Minh", ToNickname = string.Empty, Text = "tiếng Việt ✓", SentAtMs = 1_792_000_000_000 };
        using ChatResult result = new() { Status = ChatStatus.RateLimited };

        SendChat sent = SendChat.Deserialize(send.Serialize());
        ChatMessage received = ChatMessage.Deserialize(message.Serialize());

        sent.Channel.Should().Be(ChatChannel.Whisper);
        sent.Target.Should().Be("Lan Anh");
        sent.Text.Should().Be("Chào bạn 😀 — hẹn gặp ở chợ");
        received.Should().BeEquivalentTo(new { Channel = ChatChannel.Coop, FromNickname = "Minh", ToNickname = string.Empty, Text = "tiếng Việt ✓", SentAtMs = 1_792_000_000_000 });
        ChatResult.Deserialize(result.Serialize()).Status.Should().Be(ChatStatus.RateLimited);
    }

    [Fact]
    public void BlockedList_FriendsSnapshot_AndMailList_RoundTrip()
    {
        Guid a = Guid.CreateVersion7();
        Guid b = Guid.CreateVersion7();
        using BlockedList blocked = new();
        blocked.Players.Add(new BlockedRow { PlayerId = a, Nickname = "Troll" });

        using FriendsSnapshot friends = new() { FriendsMayVisit = false, IsGuest = false };
        friends.Friends.Add(new FriendRow { PlayerId = a, Nickname = "Lan", Online = true, MapId = "town" });
        friends.Incoming.Add(new FriendRequestRow { PlayerId = b, Nickname = "Hoa" });
        friends.Outgoing.Add(new FriendRequestRow { PlayerId = a, Nickname = "Nam" });

        using MailList mail = new() { Unread = 2 };
        MailRow row = new() { Id = a, Code = "market.sold", SentAtMs = 5, ExpiresAtMs = 9, Read = true, Coins = 120, Collected = false };
        row.Args.Add(new MailArgRow { Value = "42" });
        row.Args.Add(new MailArgRow { Value = "3" });
        row.Items.Add(new MailItemRow { ItemId = 7, Quantity = 4 });
        mail.Mails.Add(row);

        BlockedList.Deserialize(blocked.Serialize()).Players.Single().Should().BeEquivalentTo(new { PlayerId = a, Nickname = "Troll" });

        FriendsSnapshot f = FriendsSnapshot.Deserialize(friends.Serialize());
        f.FriendsMayVisit.Should().BeFalse();
        f.Friends.Single().Should().BeEquivalentTo(new { PlayerId = a, Nickname = "Lan", Online = true, MapId = "town" });
        f.Incoming.Single().Nickname.Should().Be("Hoa");
        f.Outgoing.Single().Nickname.Should().Be("Nam");

        MailList m = MailList.Deserialize(mail.Serialize());
        m.Unread.Should().Be(2);
        MailRow copy = m.Mails.Single();
        copy.Should().BeEquivalentTo(new { Id = a, Code = "market.sold", SentAtMs = 5L, ExpiresAtMs = 9L, Read = true, Coins = 120L, Collected = false });
        copy.Args.Select(x => x.Value).Should().Equal("42", "3");
        copy.Items.Single().Should().BeEquivalentTo(new { ItemId = 7, Quantity = 4 });
    }

    [Fact]
    public void FriendRequestsAndMailRequests_RoundTrip()
    {
        Guid id = Guid.CreateVersion7();
        using RequestFriend request = new() { Nickname = "Hoàng Yến" };
        using RespondFriend respond = new() { PlayerId = id, Accept = true };
        using FriendStatusChanged status = new() { PlayerId = id, Online = true, MapId = "beach" };
        using CollectMail collect = new() { MailId = id };
        using MailUnread unread = new() { Count = 9 };

        RequestFriend.Deserialize(request.Serialize()).Nickname.Should().Be("Hoàng Yến");
        RespondFriend.Deserialize(respond.Serialize()).Should().BeEquivalentTo(new { PlayerId = id, Accept = true });
        FriendStatusChanged.Deserialize(status.Serialize()).Should().BeEquivalentTo(new { PlayerId = id, Online = true, MapId = "beach" });
        CollectMail.Deserialize(collect.Serialize()).MailId.Should().Be(id);
        MailUnread.Deserialize(unread.Serialize()).Count.Should().Be(9);
    }

    private static ushort[] Values(Type opCodes)
        => [.. opCodes.GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (ushort)f.GetRawConstantValue()!)];
}

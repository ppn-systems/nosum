// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Chat;
using Nosum.Contracts.Chat.C2S;
using Nosum.Contracts.Chat.S2C;
using Nosum.Contracts.Friends;
using Nosum.Contracts.Friends.C2S;
using Nosum.Contracts.Friends.S2C;
using Nosum.Contracts.Visit;
using Nosum.Contracts.Visit.C2S;
using Nosum.Contracts.Visit.S2C;
using Nosum.Gameplay.Maps;
using Nosum.Wasm.Socket.Chat;
using Nosum.Wasm.Socket.Friends;
using Nosum.Wasm.Socket.Visit;
using Nosum.Wasm.ViewModels;
using Nosum.Wasm.ViewModels.Friends;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

[SuppressMessage("Reliability", "CA2000", Justification = "Packets and channels here are plain objects over a fake socket that dies with the test; nothing is rented from a pool.")]
public sealed class FriendsTests
{
    private static readonly Guid s_lan = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_minh = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid s_ask = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static FriendsSnapshot Snapshot()
    {
        FriendsSnapshot snapshot = new() { FriendsMayVisit = true, IsGuest = false };
        snapshot.Friends.Add(new FriendRow { PlayerId = s_lan, Nickname = "Lan Anh", Online = false, MapId = string.Empty });
        snapshot.Friends.Add(new FriendRow { PlayerId = s_minh, Nickname = "Minh", Online = true, MapId = GameMaps.Town });
        snapshot.Incoming.Add(new FriendRequestRow { PlayerId = s_ask, Nickname = "guest-0042" });
        return snapshot;
    }

    [Fact]
    public void Load_StoresFriendsAndRequests()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Snapshot());
        using FriendsChannel channel = new(socket);

        channel.Load();

        channel.Current.FriendsMayVisit.Should().BeTrue();
        channel.Current.Friends.Select(f => f.PlayerId).Should().Equal(s_lan, s_minh);
        channel.Current.Incoming.Should().Equal(new FriendRequestInfo(s_ask, "guest-0042"));
    }

    [Fact]
    public void StatusPush_FriendWentOnline_ChangesThatRowInPlaceWithoutARequest()
    {
        FakeGameSocket socket = new();
        socket.Push(Snapshot());
        using FriendsChannel channel = new(socket);
        socket.Push(Snapshot());
        int changed = 0;
        channel.Changed += () => changed++;

        socket.Push(new FriendStatusChanged { PlayerId = s_lan, Online = true, MapId = GameMaps.Forest });

        channel.Current.Friends.Single(f => f.PlayerId == s_lan).Should().Be(new FriendInfo(s_lan, "Lan Anh", true, GameMaps.Forest));
        channel.Current.Friends.Single(f => f.PlayerId == s_minh).Online.Should().BeTrue("the other row is untouched");
        changed.Should().Be(1);
        socket.Requests.Should().BeEmpty("a status push is applied without refetching");
    }

    [Fact]
    public void StatusPush_FriendWentOffline_ClearsTheMap()
    {
        FakeGameSocket socket = new();
        using FriendsChannel channel = new(socket);
        socket.Push(Snapshot());

        socket.Push(new FriendStatusChanged { PlayerId = s_minh, Online = false, MapId = GameMaps.Town });

        channel.Current.Friends.Single(f => f.PlayerId == s_minh).Should().Be(new FriendInfo(s_minh, "Minh", false, string.Empty));
    }

    [Fact]
    public void StatusPush_UnknownPlayer_IsIgnored()
    {
        FakeGameSocket socket = new();
        using FriendsChannel channel = new(socket);
        socket.Push(Snapshot());
        int changed = 0;
        channel.Changed += () => changed++;

        socket.Push(new FriendStatusChanged { PlayerId = Guid.NewGuid(), Online = true, MapId = GameMaps.Town });

        changed.Should().Be(0);
        channel.Current.Friends.Should().HaveCount(2);
    }

    [Fact]
    public void Friends_OnlineFirst_WithLocalizedNamesAndPlaces()
    {
        FakeGameSocket socket = new();
        using FriendsChannel channel = new(socket);
        socket.Push(Snapshot());
        using FriendsViewModel vm = Create(socket, channel);

        IReadOnlyList<FriendDisplay> rows = vm.Friends;

        rows.Select(r => r.PlayerId).Should().Equal(s_minh, s_lan);
        rows[0].Online.Should().BeTrue();
        rows[0].Where.Should().NotBeNullOrEmpty().And.NotStartWith("Friends_");
        rows[1].Where.Should().NotBe(rows[0].Where);
        vm.Incoming.Single().Label.Should().Contain("0042").And.NotContain("guest-");
    }

    [Fact]
    public void Request_TypedNickname_IsSentAndTheListReadAgain()
    {
        FakeGameSocket socket = new();
        List<string> asked = [];
        socket.ReplyWith(request =>
        {
            asked.Add(((RequestFriend)request).Nickname);
            return new FriendResult { Status = FriendStatus.Ok };
        });
        socket.ReplyWith(_ => Snapshot());
        using FriendsChannel channel = new(socket);
        using FriendsViewModel vm = Create(socket, channel);
        vm.AddText = "  Guest 0042 ";

        vm.Request();

        asked.Should().Equal("guest-0042");
        vm.AddText.Should().BeEmpty();
        socket.Requests.OfType<GetFriends>().Should().ContainSingle();
        channel.Current.Friends.Should().HaveCount(2);
    }

    [Fact]
    public void Respond_AcceptAndDecline_SendTheAnswerForThatPlayer()
    {
        FakeGameSocket socket = new();
        List<(Guid, bool)> answers = [];
        socket.ReplyWith(request =>
        {
            RespondFriend respond = (RespondFriend)request;
            answers.Add((respond.PlayerId, respond.Accept));
            return new FriendResult { Status = FriendStatus.Ok };
        });
        socket.ReplyWith(_ => Snapshot());
        using FriendsChannel channel = new(socket);
        using FriendsViewModel vm = Create(socket, channel);

        vm.Accept(s_ask);
        vm.Decline(s_ask);

        answers.Should().Equal((s_ask, true), (s_ask, false));
    }

    [Fact]
    public void ToggleVisit_SendsTheOppositeOfTheCurrentSetting()
    {
        FakeGameSocket socket = new();
        bool? sent = null;
        socket.ReplyWith(request =>
        {
            sent = ((SetFriendsVisit)request).Allowed;
            return new FriendResult { Status = FriendStatus.Ok };
        });
        socket.ReplyWith(_ => Snapshot());
        using FriendsChannel channel = new(socket);
        socket.Push(Snapshot());
        using FriendsViewModel vm = Create(socket, channel);

        vm.ToggleVisit();

        sent.Should().Be(false);
    }

    [Fact]
    public void Refusal_ShowsTheLocalizedText()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new FriendResult { Status = FriendStatus.GuestNotAllowed });
        socket.ReplyWith(_ => Snapshot());
        using FriendsChannel channel = new(socket);
        using FriendsViewModel vm = Create(socket, channel);
        vm.AddText = "Minh";

        vm.Request();

        vm.Status.Should().Be(vm.StatusText(FriendStatus.GuestNotAllowed)).And.NotStartWith("Friend_Status_");
    }

    [Fact]
    public void Guest_SnapshotFlagIsExposed()
    {
        FakeGameSocket socket = new();
        using FriendsChannel channel = new(socket);
        socket.Push(new FriendsSnapshot { IsGuest = true });
        using FriendsViewModel vm = Create(socket, channel);

        vm.IsGuest.Should().BeTrue();
        vm.Friends.Should().BeEmpty();
    }

    [Fact]
    public void Blocklist_BlockAndUnblock_UseTheNicknameAndShowTheBlockedList()
    {
        FakeGameSocket socket = new();
        List<string> calls = [];
        List<BlockedRow> rows = [];
        socket.ReplyWith(request =>
        {
            calls.Add(request is BlockPlayer block ? "block " + block.Nickname : "unblock " + ((UnblockPlayer)request).Nickname);
            return new ChatResult { Status = ChatStatus.Ok };
        });
        socket.ReplyWith(_ =>
        {
            BlockedList list = new();
            list.Players.AddRange(rows);
            return list;
        });
        using FriendsChannel channel = new(socket);
        using FriendsViewModel vm = Create(socket, channel);
        rows.Add(new BlockedRow { PlayerId = Guid.NewGuid(), Nickname = "guest-0007" });
        vm.BlockText = "Guest 0007";

        vm.Block();
        string label = vm.Blocked.Single().Label;
        rows.Clear();
        vm.Unblock("guest-0007");

        calls.Should().Equal("block guest-0007", "unblock guest-0007");
        label.Should().Contain("0007").And.NotContain("guest-");
        vm.Blocked.Should().BeEmpty();
        vm.Status.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Language_EveryFriendStatusAndPublicMapHasATextInBothLanguages()
    {
        List<string> needed = [];
        needed.AddRange(Enum.GetValues<FriendStatus>().Select(s => "Friend_Status_" + s));
        // The floors of the mine are private places: the server never sends their id (docs/adr/ADR-029 D4), so only the cave is named.
        needed.AddRange(GameMaps.All.Where(m => !m.IsMineFloor).Select(m => "Friends_Map_" + m.Id));

        SocialClientFixtures.Keys("Language.resx").Should().Contain(needed);
        SocialClientFixtures.Keys("Language.en.resx").Should().Contain(needed);
    }

    [Fact]
    public void Items_FriendsAndMailAreMenuTabsBeforeTheProfile_SettingsStaysLast()
    {
        ToolbarViewModel toolbar = new(SocialClientFixtures.Localizer<Language>());

        toolbar.Items.Select(i => i.Kind).Should().ContainInOrder(PanelKind.Friends, PanelKind.Mail, PanelKind.Profile);
        toolbar.Items[^1].Kind.Should().Be(PanelKind.Settings);
        toolbar.Items.Where(i => i.Kind is PanelKind.Friends or PanelKind.Mail).Should().OnlyContain(i => !i.Label.StartsWith("Toolbar_", StringComparison.Ordinal));
    }

    [Fact]
    public void Visit_AFriend_SendsTheVisitWithTheFriendsStoredNickname()
    {
        FakeGameSocket socket = new();
        List<string> asked = [];
        // Pooled packets are cleared after the call, so the nickname is read while the request is still answered.
        socket.ReplyWith(request =>
        {
            asked.Add(((VisitPlayer)request).Nickname);
            return new VisitResult { Status = VisitStatus.Ok };
        });
        using FriendsChannel channel = new(socket);
        socket.Push(Snapshot());
        using FriendsViewModel vm = Create(socket, channel);

        vm.Visit(s_lan);

        asked.Should().Equal("Lan Anh");
    }

    [Fact]
    public void Visit_AnUnknownPlayer_SendsNothing()
    {
        FakeGameSocket socket = new();
        using FriendsChannel channel = new(socket);
        socket.Push(Snapshot());
        using FriendsViewModel vm = Create(socket, channel);

        vm.Visit(s_ask);

        socket.Requests.OfType<VisitPlayer>().Should().BeEmpty();
    }

    [Fact]
    public void Visit_Accepted_RaisesEnteredSoTheMenuCloses()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new VisitResult { Status = VisitStatus.Ok });
        using FriendsChannel channel = new(socket);
        socket.Push(Snapshot());
        using FriendsViewModel vm = Create(socket, channel);
        int entered = 0;
        vm.Entered += () => entered++;

        vm.Visit(s_minh);

        entered.Should().Be(1);
        vm.Status.Should().BeEmpty();
    }

    [Fact]
    public void Visit_Refused_ShowsTheReasonAndDoesNotLeaveThePage()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new VisitResult { Status = VisitStatus.NotAllowed });
        using FriendsChannel channel = new(socket);
        socket.Push(Snapshot());
        using FriendsViewModel vm = Create(socket, channel);
        int entered = 0;
        vm.Entered += () => entered++;

        vm.Visit(s_minh);

        entered.Should().Be(0);
        vm.Status.Should().NotBeNullOrEmpty().And.NotStartWith("Visit_Status_");
    }

    [Fact]
    public void Language_VisitButtonAndTouchChatHaveTextInBothLanguages()
    {
        string[] needed = ["Friends_VisitFarm", "Touch_Chat", "Touch_ChatLabel"];

        SocialClientFixtures.Keys("Language.resx").Should().Contain(needed);
        SocialClientFixtures.Keys("Language.en.resx").Should().Contain(needed);
    }

    private static FriendsViewModel Create(FakeGameSocket socket, FriendsChannel channel)
        => new(channel, new ChatMessenger(socket), new VisitChannel(socket), SocialClientFixtures.Names(socket), SocialClientFixtures.Localizer<Language>());
}

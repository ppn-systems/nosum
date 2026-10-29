// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Friends;
using Nosum.Contracts.Account;
using Nosum.Contracts.Chat;
using Nosum.Contracts.Common;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Friends;
using Nosum.Contracts.Trade;
using Nosum.Contracts.Visit;
using Nosum.Core;
using Nosum.Domain.Coops;
using Nosum.Domain.Social;
using Nosum.Gameplay.Chat;
using Nosum.Gameplay.Maps;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Chat, the block list and reports over the wire (docs/adr/ADR-029 D1-D3): real Nalix WebSocket clients against the real host and a real
/// PostgreSQL database. The point of these tests is who does NOT receive a message: other maps, third parties of a whisper, players who
/// blocked the sender. Every sender stays under the burst of three messages, which is the rule they do not test.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class ChatWireTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_silence = TimeSpan.FromMilliseconds(1_300);

    private readonly PostgresFixture _database;

    public ChatWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task LocalChat_ReachesThePlayersOfTheSendersPlaceOnly_CleanedAndMasked()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        Account carol = await RegisterAsync();
        await PutOnAsync(alice, GameMaps.Town);
        await PutOnAsync(bob, GameMaps.Town);
        await PutOnAsync(carol, GameMaps.BusStop);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        await using SocialLifeClient b = await host.ConnectAsync(bob);
        await using SocialLifeClient c = await host.ConnectAsync(carol);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);
        _ = await c.JoinAsync(host.Timeout);

        (await a.SendAsync(ChatChannel.Local, string.Empty, "  hello\t  world you fuck  ", host.Timeout)).Should().Be(ChatStatus.Ok);

        ChatCopy heard = await b.Chats.WaitAsync(host.Timeout);
        heard.Should().Be(new ChatCopy(ChatChannel.Local, alice.Nickname, string.Empty, "hello world you ****"));
        (await a.Chats.WaitAsync(host.Timeout)).Should().Be(heard, "the sender gets their own line back");
        await c.Chats.ExpectNoneAsync(_ => true, s_silence, host.Timeout);
    }

    [Fact]
    public async Task MapChat_ReachesTheWholeInstance_AndNoOtherMap()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        Account carol = await RegisterAsync();
        await PutOnAsync(alice, GameMaps.BusStop);
        await PutOnAsync(bob, GameMaps.BusStop);
        await PutOnAsync(carol, GameMaps.Town);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        await using SocialLifeClient b = await host.ConnectAsync(bob);
        await using SocialLifeClient c = await host.ConnectAsync(carol);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);
        _ = await c.JoinAsync(host.Timeout);

        (await b.SendAsync(ChatChannel.Map, "ignored target", "Xin chào cả bản đồ", host.Timeout)).Should().Be(ChatStatus.Ok);

        (await a.Chats.WaitAsync(host.Timeout)).Should().Be(new ChatCopy(ChatChannel.Map, bob.Nickname, string.Empty, "Xin chào cả bản đồ"));
        await c.Chats.ExpectNoneAsync(_ => true, s_silence, host.Timeout);
    }

    [Fact]
    public async Task Whisper_GoesToTheAddresseeAndEchoesToTheSender_NeverToAThirdParty()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        Account carol = await RegisterAsync();
        foreach (Account account in new[] { alice, bob, carol })
        {
            await PutOnAsync(account, GameMaps.Town);
        }

        await using SocialLifeClient a = await host.ConnectAsync(alice);
        await using SocialLifeClient b = await host.ConnectAsync(bob);
        await using SocialLifeClient c = await host.ConnectAsync(carol);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);
        _ = await c.JoinAsync(host.Timeout);

        (await a.SendAsync(ChatChannel.Whisper, bob.Nickname.ToUpperInvariant(), "secret plan", host.Timeout)).Should().Be(ChatStatus.Ok);

        ChatCopy expected = new(ChatChannel.Whisper, alice.Nickname, bob.Nickname, "secret plan");
        (await b.Chats.WaitAsync(host.Timeout)).Should().Be(expected);
        (await a.Chats.WaitAsync(host.Timeout)).Should().Be(expected, "the sender's echo names the addressee");
        await c.Chats.ExpectNoneAsync(_ => true, s_silence, host.Timeout);

        // And the reply travels the other way, again past carol.
        (await b.SendAsync(ChatChannel.Whisper, alice.Nickname, "agreed", host.Timeout)).Should().Be(ChatStatus.Ok);
        (await a.Chats.WaitAsync(m => m.Text == "agreed", host.Timeout)).From.Should().Be(bob.Nickname);
        await c.Chats.ExpectNoneAsync(_ => true, s_silence, host.Timeout);
    }

    [Fact]
    public async Task Whisper_ToAnAddresseeWhoCannotBeReached_SaysWhy_AndSendsNothing()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account offline = await RegisterAsync();
        await PutOnAsync(alice, GameMaps.Town);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        _ = await a.JoinAsync(host.Timeout);

        (await a.SendAsync(ChatChannel.Whisper, offline.Nickname, "hello?", host.Timeout)).Should().Be(ChatStatus.TargetOffline);
        (await a.SendAsync(ChatChannel.Whisper, TestNicknames.Next(), "hello?", host.Timeout)).Should().Be(ChatStatus.UnknownPlayer);
        (await a.SendAsync(ChatChannel.Whisper, "no!", "hello?", host.Timeout)).Should().Be(ChatStatus.InvalidNickname);
        (await a.SendAsync(ChatChannel.Whisper, string.Empty, "hello?", host.Timeout)).Should().Be(ChatStatus.InvalidNickname);
        (await a.SendAsync(ChatChannel.Whisper, alice.Nickname, "hello me", host.Timeout)).Should().Be(ChatStatus.SelfWhisper);
        (await a.SendAsync(ChatChannel.Local, string.Empty, string.Concat(" \t", ((char)0x200B).ToString(), " "), host.Timeout)).Should().Be(ChatStatus.Empty);
        (await a.SendAsync((ChatChannel)9, string.Empty, "hi", host.Timeout)).Should().Be(ChatStatus.InvalidChannel);

        await a.Chats.ExpectNoneAsync(_ => true, s_silence, host.Timeout);
    }

    [Fact]
    public async Task Block_DropsChatWhisperTradeAndVisitRequests_AndUnblockingRestoresThem()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        Account carol = await RegisterAsync();
        foreach (Account account in new[] { alice, bob, carol })
        {
            await PutOnAsync(account, GameMaps.Town);
        }

        await using SocialLifeClient a = await host.ConnectAsync(alice);
        await using SocialLifeClient b = await host.ConnectAsync(bob);
        await using SocialLifeClient c = await host.ConnectAsync(carol);
        _ = await a.JoinAsync(host.Timeout);
        Nosum.Contracts.Player.S2C.JoinMapResult bobJoin = await b.JoinAsync(host.Timeout);
        _ = await c.JoinAsync(host.Timeout);

        // Before the block: Bob lists Alice as a visitor and they are friends; a trade request to Bob is open (so Bob would be "busy").
        (await b.AddVisitorAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok);
        IFriendService friends = InstanceManager.Instance.GetExistingInstance<IFriendService>()!;
        (await friends.RequestAsync(alice.Id, bob.Nickname)).Should().Be(FriendStatus.Ok);
        (await friends.RespondAsync(bob.Id, alice.Id, true)).Should().Be(FriendStatus.Ok);
        (await c.TradeRequestAsync(bobJoin.PlayerId, host.Timeout)).Should().Be(TradeStatus.Ok);
        (await b.TradeInvites.WaitAsync(host.Timeout)).Should().Be(c.MapPlayerId);

        // The block, over the wire; the nickname matches whatever the case.
        (await b.BlockAsync(alice.Nickname.ToUpperInvariant(), host.Timeout)).Should().Be(ChatStatus.Ok);
        (await b.BlockAsync(alice.Nickname, host.Timeout)).Should().Be(ChatStatus.AlreadyBlocked);
        (await b.BlockAsync(TestNicknames.Next(), host.Timeout)).Should().Be(ChatStatus.UnknownPlayer);
        (await b.BlockAsync(bob.Nickname, host.Timeout)).Should().Be(ChatStatus.SelfBlock);
        (await b.BlockAsync("no!", host.Timeout)).Should().Be(ChatStatus.InvalidNickname);
        (await b.GetBlockedAsync(host.Timeout)).Should().ContainSingle().Which.Should().Be((alice.Id, alice.Nickname));
        (await friends.AreFriendsAsync(alice.Id, bob.Id)).Should().BeFalse("blocking ends the friendship");
        (await friends.AreFriendsAsync(bob.Id, alice.Id)).Should().BeFalse();

        // Chat: Alice thinks she is talking; Bob hears nothing, Carol hears it.
        (await a.SendAsync(ChatChannel.Local, string.Empty, "hello everyone", host.Timeout)).Should().Be(ChatStatus.Ok);
        (await c.Chats.WaitAsync(host.Timeout)).Text.Should().Be("hello everyone");
        await b.Chats.ExpectNoneAsync(m => m.From == alice.Nickname, s_silence, host.Timeout);

        // Whispers: to Bob she looks as if he is offline; Bob is told honestly that he blocked her.
        (await a.SendAsync(ChatChannel.Whisper, bob.Nickname, "please?", host.Timeout)).Should().Be(ChatStatus.TargetOffline);
        (await b.SendAsync(ChatChannel.Whisper, alice.Nickname, "no", host.Timeout)).Should().Be(ChatStatus.Blocked);

        // Trade: both directions are dropped before Bob's open invitation is even looked at.
        (await a.TradeRequestAsync(bobJoin.PlayerId, host.Timeout)).Should().Be(TradeStatus.TargetNotFound);
        (await b.TradeRequestAsync(a.MapPlayerId, host.Timeout)).Should().Be(TradeStatus.TargetNotFound);

        // Visit: the allow-list entry is not enough against a block.
        (await a.VisitAsync(bob.Nickname, host.Timeout)).Should().Be(VisitStatus.NotAllowed);

        // Unblocked, Alice is heard again.
        (await b.UnblockAsync(alice.Nickname, host.Timeout)).Should().Be(ChatStatus.Ok);
        (await b.UnblockAsync(alice.Nickname, host.Timeout)).Should().Be(ChatStatus.NotBlocked);
        (await b.GetBlockedAsync(host.Timeout)).Should().BeEmpty();
        await Task.Delay(2_100); // one token of the rate rule is back
        (await a.SendAsync(ChatChannel.Local, string.Empty, "back again", host.Timeout)).Should().Be(ChatStatus.Ok);
        (await b.Chats.WaitAsync(m => m.From == alice.Nickname, host.Timeout)).Text.Should().Be("back again");
    }

    [Fact]
    public async Task Guest_CanChatLocally_ButNotOnTheOtherChannels()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account guest = await StartGuestAsync();
        Account bob = await RegisterAsync();
        await PutOnAsync(guest, GameMaps.Town);
        await PutOnAsync(bob, GameMaps.Town);
        await using SocialLifeClient g = await host.ConnectAsync(guest);
        await using SocialLifeClient b = await host.ConnectAsync(bob);
        _ = await g.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);

        (await g.SendAsync(ChatChannel.Whisper, bob.Nickname, "psst", host.Timeout)).Should().Be(ChatStatus.GuestLocalOnly);
        (await g.SendAsync(ChatChannel.Map, string.Empty, "to the map", host.Timeout)).Should().Be(ChatStatus.GuestLocalOnly);
        (await g.SendAsync(ChatChannel.Coop, string.Empty, "to the coop", host.Timeout)).Should().Be(ChatStatus.GuestLocalOnly);
        await b.Chats.ExpectNoneAsync(_ => true, s_silence, host.Timeout);

        (await g.SendAsync(ChatChannel.Local, string.Empty, "hi from a guest", host.Timeout)).Should().Be(ChatStatus.Ok);
        ChatCopy heard = await b.Chats.WaitAsync(host.Timeout);
        heard.From.Should().StartWith("guest-");
        heard.Text.Should().Be("hi from a guest");

        // A registered player can whisper a guest who is online (the guest just cannot whisper first).
        (await b.SendAsync(ChatChannel.Whisper, guest.Nickname, "welcome", host.Timeout)).Should().Be(ChatStatus.Ok);
        (await g.Chats.WaitAsync(m => m.Channel == ChatChannel.Whisper, host.Timeout)).Text.Should().Be("welcome");
    }

    [Fact]
    public async Task Chat_AfterTheBurstOfThree_TheRateRuleRefuses_AndATokenReturnsAfterTwoSeconds()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        await PutOnAsync(alice, GameMaps.Town);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        _ = await a.JoinAsync(host.Timeout);

        // The transport lets three requests through back to back (and refills quickly); the rule of the service is what answers "rate limited".
        (await a.SendFastAsync(ChatChannel.Local, string.Empty, "one", host.Timeout)).Should().Be(ChatStatus.Ok);
        (await a.SendFastAsync(ChatChannel.Local, string.Empty, "two", host.Timeout)).Should().Be(ChatStatus.Ok);
        (await a.SendFastAsync(ChatChannel.Local, string.Empty, "three", host.Timeout)).Should().Be(ChatStatus.Ok);
        await Task.Delay(350);
        (await a.SendFastAsync(ChatChannel.Local, string.Empty, "four", host.Timeout)).Should().Be(ChatStatus.RateLimited);

        await Task.Delay(2_200);
        (await a.SendFastAsync(ChatChannel.Local, string.Empty, "five", host.Timeout)).Should().Be(ChatStatus.Ok);
        (await a.SendFastAsync(ChatChannel.Local, string.Empty, "six", host.Timeout)).Should().Be(ChatStatus.RateLimited);

        // What was refused was not delivered, not even as an echo.
        List<string> heard = [];
        while (true)
        {
            using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(host.Timeout);
            wait.CancelAfter(s_silence);
            try
            {
                heard.Add((await a.Chats.WaitAsync(wait.Token)).Text);
            }
            catch (OperationCanceledException) when (!host.Timeout.IsCancellationRequested)
            {
                break;
            }
        }

        heard.Should().Equal("one", "two", "three", "five");
    }

    [Fact]
    public async Task CoopChat_ReachesTheOnlineMembersOnAnyMap_AndNobodyElse()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        Account carol = await RegisterAsync();
        Account sleeper = await RegisterAsync();
        await PutOnAsync(alice, GameMaps.Town);
        await PutOnAsync(bob, GameMaps.BusStop);
        await PutOnAsync(carol, GameMaps.Town);
        await CreateCoopAsync("Garden", alice, bob, sleeper);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        await using SocialLifeClient b = await host.ConnectAsync(bob);
        await using SocialLifeClient c = await host.ConnectAsync(carol);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);
        _ = await c.JoinAsync(host.Timeout);

        (await a.SendAsync(ChatChannel.Coop, string.Empty, "harvest at dawn", host.Timeout)).Should().Be(ChatStatus.Ok);

        (await b.Chats.WaitAsync(host.Timeout)).Should().Be(new ChatCopy(ChatChannel.Coop, alice.Nickname, string.Empty, "harvest at dawn"));
        await c.Chats.ExpectNoneAsync(_ => true, s_silence, host.Timeout);
        (await c.SendAsync(ChatChannel.Coop, string.Empty, "let me in", host.Timeout)).Should().Be(ChatStatus.NotInCoop);
    }

    [Fact]
    public async Task Report_StoresTheLastMessagesTheReporterReceivedAsContext()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        Account carol = await RegisterAsync();
        foreach (Account account in new[] { alice, bob, carol })
        {
            await PutOnAsync(account, GameMaps.Town);
        }

        await using SocialLifeClient a = await host.ConnectAsync(alice);
        await using SocialLifeClient b = await host.ConnectAsync(bob);
        await using SocialLifeClient c = await host.ConnectAsync(carol);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);
        _ = await c.JoinAsync(host.Timeout);
        (await a.SendAsync(ChatChannel.Local, string.Empty, "first", host.Timeout)).Should().Be(ChatStatus.Ok);
        (await a.SendAsync(ChatChannel.Local, string.Empty, "you are a bitch", host.Timeout)).Should().Be(ChatStatus.Ok);
        (await a.SendAsync(ChatChannel.Whisper, bob.Nickname, "whispered insult", host.Timeout)).Should().Be(ChatStatus.Ok);
        _ = await b.Chats.WaitAsync(m => m.Channel == ChatChannel.Whisper, host.Timeout);

        (await b.ReportAsync(carol.Nickname, host.Timeout)).Should().Be(ChatStatus.NothingToReport, "Carol wrote nothing Bob received");
        (await b.ReportAsync(bob.Nickname, host.Timeout)).Should().Be(ChatStatus.SelfBlock);
        (await b.ReportAsync(TestNicknames.Next(), host.Timeout)).Should().Be(ChatStatus.UnknownPlayer);
        (await b.ReportAsync(alice.Nickname, host.Timeout)).Should().Be(ChatStatus.Ok);

        await using NosumDbContext db = new(_database.Options);
        ChatReport report = await db.ChatReports.SingleAsync(r => r.ReporterId == bob.Id);
        report.ReportedId.Should().Be(alice.Id);
        List<ChatReportLine> lines = await db.ChatReportLines.Where(l => l.ReportId == report.Id).OrderBy(l => l.Sequence).ToListAsync();
        lines.Select(l => l.Text).Should().Equal("first", "you are a *****", "whispered insult");
        lines.Select(l => (ChatChannel)l.Channel).Should().Equal(ChatChannel.Local, ChatChannel.Local, ChatChannel.Whisper);
        lines.Should().OnlyContain(l => l.SenderId == alice.Id && l.SenderNickname == alice.Nickname);
        (await db.ChatReports.CountAsync(r => r.ReporterId == carol.Id || r.ReporterId == alice.Id)).Should().Be(0);
        ChatRules.ReportContextLines.Should().BeGreaterThanOrEqualTo(lines.Count);
    }

    [Fact]
    public async Task CoopInvite_AlsoLeavesMailForTheInvitedPlayer_WithTheCoopAndTheInviter()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        await PutOnAsync(alice, GameMaps.Town);
        await PutOnAsync(bob, GameMaps.Town);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        await using SocialLifeClient b = await host.ConnectAsync(bob);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);

        (await a.CreateCoopAsync("Vườn Xanh", host.Timeout)).Should().Be(CoopStatus.Ok);
        (await a.InviteToCoopAsync(b.MapPlayerId, host.Timeout)).Should().Be(CoopStatus.Ok);

        (await b.Unread.WaitAsync(host.Timeout)).Should().Be(1);
        (int unread, MailCopy[] mails) = await b.GetMailAsync(host.Timeout);
        unread.Should().Be(1);
        MailCopy mail = mails.Should().ContainSingle().Subject;
        mail.Code.Should().Be("coop.invite");
        mail.Args.Should().Equal("Vườn Xanh", alice.Nickname);
        mail.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task CoopInvite_FromABlockedPlayer_IsDropped()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        await PutOnAsync(alice, GameMaps.Town);
        await PutOnAsync(bob, GameMaps.Town);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        await using SocialLifeClient b = await host.ConnectAsync(bob);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);
        (await b.BlockAsync(alice.Nickname, host.Timeout)).Should().Be(ChatStatus.Ok);
        (await a.CreateCoopAsync("Garden", host.Timeout)).Should().Be(CoopStatus.Ok);

        (await a.InviteToCoopAsync(b.MapPlayerId, host.Timeout)).Should().Be(CoopStatus.TargetNotFound);

        (await b.GetMailAsync(host.Timeout)).Mails.Should().BeEmpty();
    }

    // ---- helpers ----

    private async Task CreateCoopAsync(string name, Account founder, params Account[] members)
    {
        await using NosumDbContext db = new(_database.Options);
        Coop coop = new() { Name = name, CreatedAt = DateTimeOffset.UtcNow };
        db.Coops.Add(coop);
        db.CoopMembers.Add(new CoopMember { CoopId = coop.Id, PlayerId = founder.Id, Role = CoopRole.Founder, JoinedAt = DateTimeOffset.UtcNow });
        foreach (Account member in members.Where(m => m.Id != founder.Id))
        {
            db.CoopMembers.Add(new CoopMember { CoopId = coop.Id, PlayerId = member.Id, Role = CoopRole.Member, JoinedAt = DateTimeOffset.UtcNow });
        }

        _ = await db.SaveChangesAsync();
    }

    private static async Task<Account> RegisterAsync()
    {
        IAccountService accounts = InstanceManager.Instance.GetExistingInstance<IAccountService>()!;
        AccountOutcome outcome = await TestAccounts.RegisterAsync(accounts, Guid.NewGuid().ToString("N"), TestNicknames.Next(), GameLanguage.English, CancellationToken.None);
        outcome.Status.Should().Be(AccountStatus.Ok);
        return new Account(outcome.PlayerId, outcome.SessionToken, outcome.Nickname);
    }

    private static async Task<Account> StartGuestAsync()
    {
        IAccountService accounts = InstanceManager.Instance.GetExistingInstance<IAccountService>()!;
        AccountOutcome outcome = await accounts.StartGuestAsync(GameLanguage.English, CancellationToken.None);
        outcome.Status.Should().Be(AccountStatus.Ok);
        return new Account(outcome.PlayerId, outcome.SessionToken, outcome.Nickname);
    }

    // Saves where the player was last, so the server's join puts them there.
    private static async Task PutOnAsync(Account account, string mapId)
    {
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
        await states.SaveAsync(account.Id, new PlayerStateRow(mapId, -1, -1, Facing.Down), CancellationToken.None);
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private sealed record Account(Guid Id, string Token, string Nickname);

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly NetworkApplication _app;
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(180));

        private TestHost(NetworkApplication app, ushort port)
        {
            _app = app;
            this.Port = port;
        }

        public ushort Port { get; }

        public CancellationToken Timeout => _timeout.Token;

        public static async Task<TestHost> StartAsync(PostgresFixture database, TimeProvider? clock = null)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);
            TestAccounts.UseFakeVerifiers(database.Options);
            NetworkApplication app = Program.BuildApplication(port, clock);
            TestHost host = new(app, port);
            await app.ActivateAsync(host.Timeout);
            return host;
        }

        public Task<SocialLifeClient> ConnectAsync(Account account) => SocialLifeClient.ConnectAsync(this.Port, account.Token, this.Timeout);

        public async ValueTask DisposeAsync()
        {
            await _app.DeactivateAsync(CancellationToken.None);
            await _app.DisposeAsync();
            _timeout.Dispose();
        }
    }
}

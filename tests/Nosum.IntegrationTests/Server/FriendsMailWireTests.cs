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
using Nosum.Abstractions.Friends;
using Nosum.Abstractions.Mail;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Contracts.Friends;
using Nosum.Contracts.Mail;
using Nosum.Contracts.Visit;
using Nosum.Core;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Mail;
using Nosum.Gameplay.Maps;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Friends and mail over the wire (docs/adr/ADR-029 D4-D5): real Nalix WebSocket clients against the real host and a real PostgreSQL database.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class FriendsMailWireTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _database;

    public FriendsMailWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Friends_RequestAcceptAndRemove_ShowTheListsOnlineStatusAndPublicMap()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        await PutOnAsync(alice, GameMaps.Town);
        await PutOnAsync(bob, GameMaps.BusStop);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        await using SocialLifeClient b = await host.ConnectAsync(bob);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);

        FriendsCopy empty = await a.GetFriendsAsync(host.Timeout);
        empty.Should().BeEquivalentTo(new { FriendsMayVisit = true, IsGuest = false });
        empty.Friends.Should().BeEmpty();

        (await a.RequestFriendAsync(bob.Nickname.ToUpperInvariant(), host.Timeout)).Should().Be(FriendStatus.Ok);
        (await a.RequestFriendAsync(bob.Nickname, host.Timeout)).Should().Be(FriendStatus.AlreadyRequested);

        // Bob is told by a pushed snapshot; Alice sees the request as outgoing.
        FriendsCopy bobSees = await b.FriendSnapshots.WaitAsync(host.Timeout);
        bobSees.Incoming.Should().Equal(alice.Id);
        (await a.GetFriendsAsync(host.Timeout)).Outgoing.Should().Equal(bob.Id);

        (await b.RespondFriendAsync(Guid.CreateVersion7(), true, host.Timeout)).Should().Be(FriendStatus.NoRequest);
        (await b.RespondFriendAsync(alice.Id, true, host.Timeout)).Should().Be(FriendStatus.Ok);

        FriendsCopy aliceSees = await a.FriendSnapshots.WaitAsync(s => s.Friends.Length == 1, host.Timeout);
        aliceSees.Friends.Single().Should().BeEquivalentTo(new { PlayerId = bob.Id, Nickname = bob.Nickname, Online = true, MapId = GameMaps.BusStop });
        aliceSees.Outgoing.Should().BeEmpty();
        FriendsCopy bobNow = await b.GetFriendsAsync(host.Timeout);
        bobNow.Friends.Single().Should().BeEquivalentTo(new { PlayerId = alice.Id, Online = true, MapId = GameMaps.Town });
        bobNow.Incoming.Should().BeEmpty();

        (await a.RequestFriendAsync(bob.Nickname, host.Timeout)).Should().Be(FriendStatus.AlreadyFriends);
        (await a.RequestFriendAsync(alice.Nickname, host.Timeout)).Should().Be(FriendStatus.Self);
        (await a.RequestFriendAsync(TestNicknames.Next(), host.Timeout)).Should().Be(FriendStatus.UnknownPlayer);
        (await a.RequestFriendAsync("no!", host.Timeout)).Should().Be(FriendStatus.InvalidNickname);

        (await a.RemoveFriendAsync(bob.Id, host.Timeout)).Should().Be(FriendStatus.Ok);
        (await a.RemoveFriendAsync(bob.Id, host.Timeout)).Should().Be(FriendStatus.NotFriends);
        (await b.FriendSnapshots.WaitAsync(s => s.Friends.Length == 0, host.Timeout)).Incoming.Should().BeEmpty();
        (await a.GetFriendsAsync(host.Timeout)).Friends.Should().BeEmpty();
    }

    [Fact]
    public async Task Friends_AskingBackAfterBeingAsked_MakesYouFriendsAtOnce_AndDecliningLeavesNothing()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        Account carol = await RegisterAsync();
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        await using SocialLifeClient b = await host.ConnectAsync(bob);
        await using SocialLifeClient c = await host.ConnectAsync(carol);

        (await a.RequestFriendAsync(bob.Nickname, host.Timeout)).Should().Be(FriendStatus.Ok);
        (await b.RequestFriendAsync(alice.Nickname, host.Timeout)).Should().Be(FriendStatus.Ok);
        FriendsCopy aliceList = await a.GetFriendsAsync(host.Timeout);
        aliceList.Friends.Select(f => f.PlayerId).Should().Equal(bob.Id);
        aliceList.Outgoing.Should().BeEmpty();

        (await c.RequestFriendAsync(alice.Nickname, host.Timeout)).Should().Be(FriendStatus.Ok);
        (await a.RespondFriendAsync(carol.Id, false, host.Timeout)).Should().Be(FriendStatus.Ok);
        (await a.GetFriendsAsync(host.Timeout)).Friends.Select(f => f.PlayerId).Should().Equal(bob.Id);
        (await c.GetFriendsAsync(host.Timeout)).Outgoing.Should().BeEmpty("a declined request is gone");
        (await a.RespondFriendAsync(carol.Id, true, host.Timeout)).Should().Be(FriendStatus.NoRequest);
    }

    [Fact]
    public async Task Friends_GuestsCannotBefriendOrBeBefriended_AndBlockedPlayersLookUnknown()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account guest = await StartGuestAsync();
        Account bob = await RegisterAsync();
        Account carol = await RegisterAsync();
        await using SocialLifeClient g = await host.ConnectAsync(guest);
        await using SocialLifeClient b = await host.ConnectAsync(bob);
        await using SocialLifeClient c = await host.ConnectAsync(carol);

        (await g.GetFriendsAsync(host.Timeout)).IsGuest.Should().BeTrue();
        (await g.RequestFriendAsync(bob.Nickname, host.Timeout)).Should().Be(FriendStatus.GuestNotAllowed);
        (await b.RequestFriendAsync(guest.Nickname, host.Timeout)).Should().Be(FriendStatus.GuestNotAllowed);
        (await g.SetFriendsVisitAsync(false, host.Timeout)).Should().Be(FriendStatus.GuestNotAllowed);

        (await c.BlockAsync(bob.Nickname, host.Timeout)).Should().Be(Nosum.Contracts.Chat.ChatStatus.Ok);
        (await b.RequestFriendAsync(carol.Nickname, host.Timeout)).Should().Be(FriendStatus.UnknownPlayer, "Carol blocked Bob");
        (await c.RequestFriendAsync(bob.Nickname, host.Timeout)).Should().Be(FriendStatus.UnknownPlayer, "and the other way round");
    }

    [Fact]
    public async Task Friends_StatusIsPushedWhenAFriendJoinsChangesPublicMapOrDisconnects()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        await MakeFriendsAsync(alice, bob);
        await PutOnAsync(alice, GameMaps.Town);
        await PutOnAsync(bob, GameMaps.Town);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        _ = await a.JoinAsync(host.Timeout);

        // Bob signs in and joins: Alice hears it with the map he is on.
        SocialLifeClient b = await host.ConnectAsync(bob);
        await using (b)
        {
            _ = await b.JoinAsync(host.Timeout);
            (await a.FriendStatuses.WaitAsync(host.Timeout)).Should().Be((bob.Id, true, GameMaps.Town));

            // A public map change is a push too, and is never mistaken for going offline in between.
            PlayerPresence bobPresence = PresenceOf(bob.Id);
            IPlayerService players = InstanceManager.Instance.GetExistingInstance<IPlayerService>()!;
            (await players.TravelAsync(bobPresence.Connection, MapInstance.Shared(GameMaps.BusStop), host.Timeout)).Should().Be(TravelStatus.Arrived);
            (await a.FriendStatuses.WaitAsync(host.Timeout)).Should().Be((bob.Id, true, GameMaps.BusStop));
            await a.FriendStatuses.ExpectNoneAsync(s => !s.Online, TimeSpan.FromMilliseconds(1_600), host.Timeout);

            // His own farm is private: friends see him online but not where.
            (await players.TravelAsync(PresenceOf(bob.Id).Connection, new MapInstance(GameMaps.Farm, bob.Id), host.Timeout)).Should().Be(TravelStatus.Arrived);
            (await a.FriendStatuses.WaitAsync(host.Timeout)).Should().Be((bob.Id, true, string.Empty));
            (await a.GetFriendsAsync(host.Timeout)).Friends.Single().Should().BeEquivalentTo(new { Online = true, MapId = string.Empty });
        }

        // Leaving every map for good tells his friends he is offline.
        (await a.FriendStatuses.WaitAsync(s => !s.Online, host.Timeout)).Should().Be((bob.Id, false, string.Empty));
        (await a.GetFriendsAsync(host.Timeout)).Friends.Single().Online.Should().BeFalse();
    }

    [Fact]
    public async Task Friends_AreAVisitSource_UntilTheOwnerTurnsItOffOrTheFriendshipEnds()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        Account stranger = await RegisterAsync();
        await MakeFriendsAsync(alice, bob);
        await PutOnAsync(bob, GameMaps.Town);
        await PutOnAsync(stranger, GameMaps.Town);
        await PutOnAsync(alice, GameMaps.Town);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        await using SocialLifeClient b = await host.ConnectAsync(bob);
        await using SocialLifeClient s = await host.ConnectAsync(stranger);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);
        _ = await s.JoinAsync(host.Timeout);

        // No allow-list entry, yet the friend gets in; the stranger does not.
        (await s.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.NotAllowed);
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok);
        (await b.MapChanges.WaitAsync(host.Timeout)).Should().Be(GameMaps.Farm);

        // Turning it off sends him out at once and keeps him out.
        (await a.SetFriendsVisitAsync(false, host.Timeout)).Should().Be(FriendStatus.Ok);
        (await b.MapChanges.WaitAsync(m => m == GameMaps.Town, host.Timeout)).Should().Be(GameMaps.Town);
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.NotAllowed);
        (await a.GetFriendsAsync(host.Timeout)).FriendsMayVisit.Should().BeFalse();

        // Turned on again, he may come; ending the friendship sends him out again.
        (await a.SetFriendsVisitAsync(true, host.Timeout)).Should().Be(FriendStatus.Ok);
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.Ok);
        (await b.MapChanges.WaitAsync(m => m == GameMaps.Farm, host.Timeout)).Should().Be(GameMaps.Farm);
        (await a.RemoveFriendAsync(bob.Id, host.Timeout)).Should().Be(FriendStatus.Ok);
        (await b.MapChanges.WaitAsync(m => m == GameMaps.Town, host.Timeout)).Should().Be(GameMaps.Town);
        (await b.VisitAsync(alice.Nickname, host.Timeout)).Should().Be(VisitStatus.NotAllowed);
    }

    [Fact]
    public async Task Mail_SystemMailWithAnAttachment_IsPushedListedReadCollectedOnceAndDeleted()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        await PutOnAsync(alice, GameMaps.Town);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        _ = await a.JoinAsync(host.Timeout);
        IMailService mail = InstanceManager.Instance.GetExistingInstance<IMailService>()!;

        // A producer (here the service itself, as community goals will) sends mail; the unread count is pushed.
        Guid reward = await mail.SendAsync(alice.Id, MailRules.Codes.GoalReward, ["spring", "corn"], new MailAttachments([new MailItem(ItemCatalog.Parsnip, 6)], 250));
        Guid notice = await mail.SendAsync(alice.Id, MailRules.Codes.MarketSold, [ItemCatalog.Parsnip.ToString(System.Globalization.CultureInfo.InvariantCulture), "3", "57"]);
        (await a.Unread.WaitAsync(host.Timeout)).Should().Be(1);
        (await a.Unread.WaitAsync(host.Timeout)).Should().Be(2);

        (int unread, MailCopy[] mails) = await a.GetMailAsync(host.Timeout);
        unread.Should().Be(2);
        mails.Select(m => m.Id).Should().Equal(notice, reward);
        MailCopy rewardMail = mails[1];
        rewardMail.Should().BeEquivalentTo(new { Code = "goal.reward", Read = false, Coins = 250L, Collected = false });
        rewardMail.Args.Should().Equal("spring", "corn");
        rewardMail.Items.Should().Equal((ItemCatalog.Parsnip, 6));

        (await a.ReadMailAsync(notice, host.Timeout)).Should().Be(MailStatus.Ok);
        (await a.Unread.WaitAsync(host.Timeout)).Should().Be(1);

        // Deleting before collecting is refused; collecting pays once; the second collect pays nothing.
        (await a.DeleteMailAsync(reward, host.Timeout)).Should().Be(MailStatus.HasAttachments);
        (await a.CollectMailAsync(reward, host.Timeout)).Should().Be(MailStatus.Ok);
        (await a.Wallet.WaitAsync(host.Timeout)).Should().Be(CoinRules.StartingCoins + 250);
        (await a.CollectMailAsync(reward, host.Timeout)).Should().Be(MailStatus.AlreadyCollected);
        (await a.CollectMailAsync(notice, host.Timeout)).Should().Be(MailStatus.NothingToCollect);
        (await a.CollectMailAsync(Guid.CreateVersion7(), host.Timeout)).Should().Be(MailStatus.NotFound);

        (await a.GetMailAsync(host.Timeout)).Mails.Single(m => m.Id == reward).Should().BeEquivalentTo(new { Read = true, Collected = true });
        (await a.DeleteMailAsync(reward, host.Timeout)).Should().Be(MailStatus.Ok);
        (await a.DeleteMailAsync(notice, host.Timeout)).Should().Be(MailStatus.Ok);
        (await a.GetMailAsync(host.Timeout)).Mails.Should().BeEmpty();
        await using NosumDbContext db = new(_database.Options);
        (await db.InventorySlots.Where(i => i.ItemId == ItemCatalog.Parsnip).SumAsync(i => i.Quantity)).Should().BeGreaterThanOrEqualTo(6);
    }

    [Fact]
    public async Task Mail_AnotherPlayersMailIdOverTheWire_IsNotFound()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account mallory = await RegisterAsync();
        await using SocialLifeClient m = await host.ConnectAsync(mallory);
        IMailService mail = InstanceManager.Instance.GetExistingInstance<IMailService>()!;
        Guid secret = await mail.SendAsync(alice.Id, MailRules.Codes.GoalReward, [], MailAttachments.OfCoins(5_000));

        (await m.ReadMailAsync(secret, host.Timeout)).Should().Be(MailStatus.NotFound);
        (await m.CollectMailAsync(secret, host.Timeout)).Should().Be(MailStatus.NotFound);
        (await m.DeleteMailAsync(secret, host.Timeout)).Should().Be(MailStatus.NotFound);
        (await m.GetMailAsync(host.Timeout)).Mails.Should().BeEmpty();
    }

    // ---- helpers ----

    private static PlayerPresence PresenceOf(Guid accountId)
    {
        IMapPresence presence = InstanceManager.Instance.GetExistingInstance<IMapPresence>()!;
        List<string> keys = [];
        presence.CopyActiveMapIds(keys);
        foreach (string key in keys)
        {
            List<PlayerPresence> here = [];
            presence.CopyPlayers(key, here);
            PlayerPresence? found = here.FirstOrDefault(p => p.AccountId == accountId);
            if (found is not null)
            {
                return found;
            }
        }

        throw new InvalidOperationException("The player is on no map.");
    }

    // Friends through the service, so tests that are about something else do not spend their request budget on it.
    private static async Task MakeFriendsAsync(Account a, Account b)
    {
        IFriendService friends = InstanceManager.Instance.GetExistingInstance<IFriendService>()!;
        (await friends.RequestAsync(a.Id, b.Nickname)).Should().Be(FriendStatus.Ok);
        (await friends.RespondAsync(b.Id, a.Id, true)).Should().Be(FriendStatus.Ok);
    }

    private static async Task<Account> RegisterAsync()
    {
        IAccountService accounts = InstanceManager.Instance.GetExistingInstance<IAccountService>()!;
        string email = "f" + Guid.NewGuid().ToString("N")[..12] + "@gmail.com";
        AccountOutcome outcome = await accounts.RegisterAsync(email, "password-1", TestNicknames.Next(), GameLanguage.English, CancellationToken.None);
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

        public static async Task<TestHost> StartAsync(PostgresFixture database)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);
            NetworkApplication app = Program.BuildApplication(port);
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

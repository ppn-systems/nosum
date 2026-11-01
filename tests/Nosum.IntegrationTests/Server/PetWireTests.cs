// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Core;
using Nosum.Contracts.Pet;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Pets;
using Nosum.Gameplay.Shops;
using Nosum.Host;
using Nosum.Abstractions.Player;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The pet companion over the wire (docs/adr/ADR-075 D3-D5, amended for paid pets): adoption (one cat or dog, a name cleaned and masked as chat does), one petting
/// per game day, and what the other players see of the pet (kind and name in the look, pushed when the pet is adopted).
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class PetWireTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_silence = TimeSpan.FromMilliseconds(1_300);

    private readonly PostgresFixture _database;

    public PetWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task GetPet_WithoutAPet_RepliesOkWithKindNone()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        await PutAtCarpenterAsync(alice, 2_000);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        _ = await a.JoinAsync(host.Timeout);

        PetCopy pet = await a.GetPetAsync(host.Timeout);

        pet.Should().Be(new PetCopy(PetStatus.Ok, (byte)PetKind.None, string.Empty, 0, false));
    }

    [Fact]
    public async Task AdoptPet_NamesTheCat_MasksProfanity_AndTheVisitorsSeeIt()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        Account bob = await RegisterAsync();
        await PutAtCarpenterAsync(alice, 2_000);
        await PutAtCarpenterAsync(bob, 0);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        await using SocialLifeClient b = await host.ConnectAsync(bob);
        _ = await a.JoinAsync(host.Timeout);
        _ = await b.JoinAsync(host.Timeout);

        PetCopy pet = await a.AdoptPetAsync((byte)PetKind.Cat, "  Fuck  Mochi  ", host.Timeout);

        pet.Should().Be(new PetCopy(PetStatus.Ok, (byte)PetKind.Cat, "**** Mochi", PetRules.StartingMood, false));
        (await b.PetLooks.WaitAsync(look => look.PlayerId == a.MapPlayerId && look.Kind == (byte)PetKind.Cat, host.Timeout))
            .Should().Be((a.MapPlayerId, (byte)PetKind.Cat, "**** Mochi"));
    }

    [Fact]
    public async Task AdoptPet_SecondAdoption_AndBadInput_AreRefused()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        await PutAtCarpenterAsync(alice, 2_000);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        _ = await a.JoinAsync(host.Timeout);

        (await a.AdoptPetAsync((byte)PetKind.None, "Rex", host.Timeout)).Status.Should().Be(PetStatus.InvalidKind);
        (await a.AdoptPetAsync((byte)PetKind.Dog, "   ", host.Timeout)).Status.Should().Be(PetStatus.InvalidName);
        (await a.AdoptPetAsync((byte)PetKind.Dog, "Rex", host.Timeout)).Status.Should().Be(PetStatus.Ok);
        (await a.AdoptPetAsync((byte)PetKind.Cat, "Mochi", host.Timeout)).Status.Should().Be(PetStatus.AlreadyHasPet);
    }

    [Fact]
    public async Task PetPet_IsOncePerGameDay_AndRaisesTheMood()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        await PutAtCarpenterAsync(alice, 2_000);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        _ = await a.JoinAsync(host.Timeout);
        _ = await a.AdoptPetAsync((byte)PetKind.Cat, "Mochi", host.Timeout);

        PetCopy first = await a.PetPetAsync(host.Timeout);
        PetCopy second = await a.PetPetAsync(host.Timeout);

        first.Should().Be(new PetCopy(PetStatus.Ok, (byte)PetKind.Cat, "Mochi", PetRules.StartingMood + PetRules.PettingMood, true));
        second.Status.Should().Be(PetStatus.AlreadyPetted);
        second.Mood.Should().Be(first.Mood, "a refused petting changes nothing");
    }

    [Fact]
    public async Task PetPet_WithoutAPet_IsRefused()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        await PutAtCarpenterAsync(alice, 2_000);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        _ = await a.JoinAsync(host.Timeout);

        (await a.PetPetAsync(host.Timeout)).Status.Should().Be(PetStatus.NoPet);
    }

    private static async Task<Account> RegisterAsync()
    {
        IAccountService accounts = InstanceManager.Instance.GetExistingInstance<IAccountService>()!;
        AccountOutcome outcome = await TestAccounts.RegisterAsync(accounts, Guid.NewGuid().ToString("N"), TestNicknames.Next(), GameLanguage.English, CancellationToken.None);
        outcome.Status.Should().Be(AccountStatus.Ok);
        return new Account(outcome.PlayerId, outcome.SessionToken, outcome.Nickname);
    }

    [Fact]
    public async Task AdoptPet_AwayFromTheCarpenter_IsRefusedAndNoCoinsMove()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        await PutOnAsync(alice, GameMaps.Town);
        await GiveCoinsAsync(alice, 2_000);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        _ = await a.JoinAsync(host.Timeout);
        long before = await BalanceAsync(alice);

        PetCopy refused = await a.AdoptPetAsync((byte)PetKind.Cat, "Mochi", host.Timeout);

        refused.Status.Should().Be(PetStatus.NotAtShop);
        (await BalanceAsync(alice)).Should().Be(before);
        (await a.GetPetAsync(host.Timeout)).Kind.Should().Be((byte)PetKind.None);
    }

    [Fact]
    public async Task AdoptPet_WithoutTheCoins_IsRefusedAndNoPetIsKept()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        await PutAtCarpenterAsync(alice, 0);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        _ = await a.JoinAsync(host.Timeout);

        // A new farmer starts with 500 coins: a cat costs 600.
        PetCopy refused = await a.AdoptPetAsync((byte)PetKind.Cat, "Mochi", host.Timeout);

        refused.Status.Should().Be(PetStatus.NotEnoughCoins);
        (await a.GetPetAsync(host.Timeout)).Kind.Should().Be((byte)PetKind.None);
    }

    [Fact]
    public async Task RehomePet_GivesBackHalfThePriceAndRemovesThePet()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Account alice = await RegisterAsync();
        await PutAtCarpenterAsync(alice, 2_000);
        await using SocialLifeClient a = await host.ConnectAsync(alice);
        _ = await a.JoinAsync(host.Timeout);
        (await a.AdoptPetAsync((byte)PetKind.Dog, "Rex", host.Timeout)).Status.Should().Be(PetStatus.Ok);
        long beforeRehome = await BalanceAsync(alice);

        PetCopy returned = await a.RehomePetAsync(host.Timeout);

        returned.Should().Be(new PetCopy(PetStatus.Ok, (byte)PetKind.None, string.Empty, 0, false));
        (await BalanceAsync(alice)).Should().Be(beforeRehome + PetRules.RehomeRefundOf((byte)PetKind.Dog));
        (await a.GetPetAsync(host.Timeout)).Kind.Should().Be((byte)PetKind.None);
        (await a.RehomePetAsync(host.Timeout)).Status.Should().Be(PetStatus.NoPet);
    }

    // Stands the player at the carpenter's counter in Town, with the coins a test needs on top of the starting purse.
    private static async Task PutAtCarpenterAsync(Account account, long coins)
    {
        ShopPoint counter = GameMaps.All.Single(m => m.Id == GameMaps.Town).Shops.Single(p => p.ShopId == ShopCatalog.CarpenterShop);
        await PutOnAsync(account, GameMaps.Town, (counter.TileX * 16) + 8, (counter.TileY * 16) + 8);
        if (coins > 0)
        {
            await GiveCoinsAsync(account, coins);
        }
    }

    private static async Task GiveCoinsAsync(Account account, long coins)
    {
        ITransferService transfers = InstanceManager.Instance.GetExistingInstance<ITransferService>()!;
        TransferStatus granted = await transfers.ExecuteAsync([new TransferLeg(TransferParty.Player(account.Id), 0, 0, coins)], "test.grant", null, CancellationToken.None);
        granted.Should().Be(TransferStatus.Ok);
    }

    private static async Task<long> BalanceAsync(Account account)
        => await InstanceManager.Instance.GetExistingInstance<ITransferService>()!.GetBalanceAsync(account.Id, CancellationToken.None);

    private static async Task PutOnAsync(Account account, string mapId, double x = -1, double y = -1)
    {
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
        await states.SaveAsync(account.Id, new PlayerStateRow(mapId, x, y, Facing.Down), CancellationToken.None);
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

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }
}

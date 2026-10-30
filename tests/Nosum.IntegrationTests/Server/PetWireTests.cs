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
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Core;
using Nosum.Contracts.Pet;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Pets;
using Nosum.Host;
using Nosum.Abstractions.Player;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The pet companion over the wire (docs/adr/ADR-075 D3-D5): adoption (one cat or dog, a name cleaned and masked as chat does), one petting
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
        await PutOnAsync(alice, GameMaps.Town);
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
        await PutOnAsync(alice, GameMaps.Town);
        await PutOnAsync(bob, GameMaps.Town);
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
        await PutOnAsync(alice, GameMaps.Town);
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
        await PutOnAsync(alice, GameMaps.Town);
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
        await PutOnAsync(alice, GameMaps.Town);
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

    private static async Task PutOnAsync(Account account, string mapId)
    {
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
        await states.SaveAsync(account.Id, new PlayerStateRow(mapId, -1, -1, Facing.Down), CancellationToken.None);
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

// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.S2C;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Skills;
using Nosum.Host;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Skill casts over real Nalix WebSocket sessions (docs/adr/ADR-056 D5). The server admits a cast only when the skill is off cooldown, the player has
/// the MP for it, and the target is in reach. A refusal spends no MP and starts no cooldown, and two casts that race land at most once. Monsters are
/// planned per map and game day, so a probe client on the woods finds the same monsters an actor will see there.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class CombatWireTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(90);
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    // A tile is 16 pixels on every map (docs/adr/ADR-022); the saved place of a player is in pixels.
    private const double TileSize = 16;

    private static readonly byte s_fireball = (byte)CombatSkillKind.Fireball;
    private static readonly byte s_whirlwind = (byte)CombatSkillKind.Whirlwind;
    private static readonly byte s_divineHeal = (byte)CombatSkillKind.DivineHeal;

    private readonly PostgresFixture _database;

    public CombatWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Fireball_AtAMonsterInReach_LandsAndSpendsItsMp()
    {
        await using Server server = await Server.StartAsync(_database);
        await using SocialClient actor = await SocialClient.ConnectAsync(server.Port, server.Token, await NearMonsterAsync(server));
        await actor.JoinAsync(server.Token);
        MonsterRow target = await MostDurableMonsterAsync(actor, server.Token);

        CastSkillResult reply = await actor.CastSkillAsync(s_fireball, target.MonsterId, server.Token);

        reply.Status.Should().Be(CombatStatus.Ok);
        ((int)reply.CooldownMs).Should().Be(SkillCatalog.Fireball.CooldownMs);
        ((int)reply.Mp).Should().Be(CombatRules.BaseMaxMp - SkillCatalog.Fireball.MpCost);
    }

    [Fact]
    public async Task Fireball_AtAMonsterOutOfReach_IsRefusedOutOfRange()
    {
        await using Server server = await Server.StartAsync(_database);
        MonstersSnapshot monsters = await ProbeMonstersAsync(server);

        // The player stands on one monster's tile; Fireball reaches 6 tiles, so the target must be farther than that from it.
        MonsterRow stand = monsters.Monsters[0];
        MonsterRow? far = monsters.Monsters.FirstOrDefault(m => ChebyshevTiles(m, stand) > SkillCatalog.Fireball.Range);
        far.Should().NotBeNull("the woods are wide enough to hold a monster beyond Fireball's reach");

        await using SocialClient player = await SocialClient.ConnectAsync(server.Port, server.Token, CenterOf(stand));
        await player.JoinAsync(server.Token);
        CastSkillResult reply = await player.CastSkillAsync(s_fireball, far!.MonsterId, server.Token);

        reply.Status.Should().Be(CombatStatus.OutOfRange);
        ((int)reply.Mp).Should().Be(CombatRules.BaseMaxMp, "a refused cast spends no MP");
        ((int)reply.CooldownMs).Should().Be(0, "a refused cast starts no cooldown");
    }

    [Fact]
    public async Task DivineHeal_AtFullHealth_IsRefusedAlreadyFull()
    {
        await using Server server = await Server.StartAsync(_database);
        await using SocialClient player = await SocialClient.ConnectAsync(server.Port, server.Token, (GameMaps.Woods, -1, -1));
        await player.JoinAsync(server.Token);

        CastSkillResult reply = await player.CastSkillAsync(s_divineHeal, 0, server.Token);

        reply.Status.Should().Be(CombatStatus.AlreadyFull);
        ((int)reply.Mp).Should().Be(CombatRules.BaseMaxMp, "a refused cast spends no MP");
        ((int)reply.CooldownMs).Should().Be(0, "a refused cast starts no cooldown");
    }

    [Fact]
    public async Task DivineHeal_WithTooLittleMp_IsRefusedNotEnoughMp()
    {
        await using Server server = await Server.StartAsync(_database);
        await using SocialClient actor = await SocialClient.ConnectAsync(server.Port, server.Token, await NearMonsterAsync(server));
        await actor.JoinAsync(server.Token);
        MonsterRow target = await MostDurableMonsterAsync(actor, server.Token);

        // Fireball (25) then Whirlwind (15) leave 10 MP, under the 20 Divine Heal costs. Each lands on the monster the player stands on, which
        // survives the Fireball (the most durable monster on the map has more HP than one Fireball deals).
        (await actor.CastSkillAsync(s_fireball, target.MonsterId, server.Token)).Status.Should().Be(CombatStatus.Ok);
        CastSkillResult whirlwind = await actor.CastSkillAsync(s_whirlwind, 0, server.Token);
        whirlwind.Status.Should().Be(CombatStatus.Ok);
        ((int)whirlwind.Mp).Should().Be(10);

        CastSkillResult reply = await actor.CastSkillAsync(s_divineHeal, 0, server.Token);

        reply.Status.Should().Be(CombatStatus.NotEnoughMp);
        ((int)reply.Mp).Should().Be(10, "a refused cast spends no MP");
    }

    [Fact]
    public async Task Fireball_RecastWhileCoolingDown_IsRefusedOnCooldownWithTheRemainingTime()
    {
        await using Server server = await Server.StartAsync(_database);
        await using SocialClient actor = await SocialClient.ConnectAsync(server.Port, server.Token, await NearMonsterAsync(server));
        await actor.JoinAsync(server.Token);
        MonsterRow target = await MostDurableMonsterAsync(actor, server.Token);

        CastSkillResult first = await actor.CastSkillAsync(s_fireball, target.MonsterId, server.Token);
        CastSkillResult second = await actor.CastSkillAsync(s_fireball, target.MonsterId, server.Token);

        first.Status.Should().Be(CombatStatus.Ok);
        second.Status.Should().Be(CombatStatus.OnCooldown);
        ((int)second.Mp).Should().Be((int)first.Mp, "a refused cast spends no MP");
        ((int)second.CooldownMs).Should().BeInRange(1, first.CooldownMs, "the refusal reports the time left, not the full cooldown");
    }

    [Fact]
    public async Task Fireball_TwoCastsSentTogether_OnlyOneLands()
    {
        await using Server server = await Server.StartAsync(_database);
        await using SocialClient actor = await SocialClient.ConnectAsync(server.Port, server.Token, await NearMonsterAsync(server));
        await actor.JoinAsync(server.Token);
        MonsterRow target = await MostDurableMonsterAsync(actor, server.Token);

        // Both requests are in flight before either reply: the cooldown is checked and taken under the same lock as the effect (ADR-056 D5).
        Task<CastSkillResult> a = actor.CastSkillUnpacedAsync(s_fireball, target.MonsterId, server.Token);
        Task<CastSkillResult> b = actor.CastSkillUnpacedAsync(s_fireball, target.MonsterId, server.Token);
        CastSkillResult[] replies = await Task.WhenAll(a, b);

        replies.Count(r => r.Status == CombatStatus.Ok).Should().Be(1, "exactly one of two racing casts lands");
        replies.Count(r => r.Status == CombatStatus.OnCooldown).Should().Be(1, "the other is refused as cooling down");
        int landed = replies.Single(r => r.Status == CombatStatus.Ok).Mp;
        landed.Should().Be(CombatRules.BaseMaxMp - SkillCatalog.Fireball.MpCost, "the MP of one cast only is spent");
    }

    // The woods monsters are planned per map and game day; a probe joins and reads them, then the actor is placed on the most durable one.
    private static async Task<(string Map, double X, double Y)> NearMonsterAsync(Server server)
    {
        MonstersSnapshot monsters = await ProbeMonstersAsync(server);
        return CenterOf(monsters.Monsters.OrderByDescending(m => m.MaxHp).First());
    }

    // A probe joins the woods and reads its monsters; the monsters are the same for every client on that map and game day.
    private static async Task<MonstersSnapshot> ProbeMonstersAsync(Server server)
    {
        await using SocialClient probe = await SocialClient.ConnectAsync(server.Port, server.Token, (GameMaps.Woods, -1, -1));
        await probe.JoinAsync(server.Token);
        MonstersSnapshot snapshot = await probe.GetMonstersAsync(server.Token);
        snapshot.Monsters.Should().NotBeEmpty("the woods has monsters for the skill tests");
        return snapshot;
    }

    // The tile centre: a place on a tile corner can touch a blocked neighbour and be refused as a saved place (PlayerService.IsLegalPlace).
    private static (string Map, double X, double Y) CenterOf(MonsterRow m)
        => (GameMaps.Woods, (m.X * TileSize) + (TileSize / 2), (m.Y * TileSize) + (TileSize / 2));

    private static int ChebyshevTiles(MonsterRow a, MonsterRow b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static async Task<MonsterRow> MostDurableMonsterAsync(SocialClient client, CancellationToken ct)
    {
        MonstersSnapshot snapshot = await client.GetMonstersAsync(ct);
        snapshot.Monsters.Should().NotBeEmpty("the woods has monsters for the skill tests");
        return snapshot.Monsters.OrderByDescending(m => m.MaxHp).First();
    }

    private static ushort GetFreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The server is returned to the test, which disposes it.")]
    private sealed class Server : IAsyncDisposable
    {
        private readonly NetworkApplication _app;
        private readonly CancellationTokenSource _timeout = new(s_timeout);

        private Server(NetworkApplication app, ushort port)
        {
            _app = app;
            this.Port = port;
        }

        public ushort Port { get; }

        public CancellationToken Token => _timeout.Token;

        public static async Task<Server> StartAsync(PostgresFixture database)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);
            TestAccounts.UseFakeVerifiers(database.Options);
            FakeTimeProvider clock = new(s_start);
            NetworkApplication app = Program.BuildApplication(port, clock, development: true);
            Server server = new(app, port);
            await app.ActivateAsync(server.Token);
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DeactivateAsync(CancellationToken.None);
            await _app.DisposeAsync();
            _timeout.Dispose();
        }
    }
}

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
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nosum.Abstractions.Combat;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.S2C;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Skills;
using Nosum.Host;
using Nosum.Infrastructure.Combat;
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

    /// <summary>
    /// The strike wind-up resolves at its end (docs/adr/ADR-097 D3): a player who stays beside an ordinary monster through the wind-up takes the hit.
    /// The monster is one with no other monster near it, so the hit can only come from this one.
    /// </summary>
    [Fact]
    public async Task StrikeWindup_PlayerStaysAdjacent_TakesTheHitWhenTheWindupEnds()
    {
        await using Server server = await Server.StartAsync(_database, monsterAi: true);
        MonsterRow monster = await IsolatedMonsterAsync(server);
        (int dx, int dy) = await OpenDirectionAsync(monster, reachTwo: false);
        await using SocialClient actor = await SocialClient.ConnectAsync(server.Port, server.Token, TileCentreAt(monster.X + dx, monster.Y + dy));
        await actor.JoinAsync(server.Token);
        VitalsState before = await actor.GetVitalsAsync(server.Token);

        (int monsterId, int delayMs) = await actor.Telegraphs.WaitAsync(t => t.MonsterId == monster.MonsterId, server.Token);

        delayMs.Should().Be(CombatRules.MonsterStrikeWindupMs, "the wind-up is the server's, not the client's");
        server.EndWindup(delayMs);
        VitalsState after = await WaitForHpBelowAsync(actor, before.Hp, server.Token);
        after.Hp.Should().BeLessThan(before.Hp, "a player still in reach when the wind-up ends takes the contact damage");
        monsterId.Should().Be(monster.MonsterId);
    }

    /// <summary>
    /// A player who steps out of reach during the wind-up takes no hit (docs/adr/ADR-097 D3). The move is accepted by the server, so the player really
    /// is two tiles from the monster when the wind-up ends; the strike is refused, not dodged by the client.
    /// </summary>
    [Fact]
    public async Task StrikeWindup_PlayerStepsOutOfReachBeforeItEnds_TakesNoHit()
    {
        await using Server server = await Server.StartAsync(_database, monsterAi: true);
        MonsterRow monster = await IsolatedMonsterAsync(server);
        (int dx, int dy) = await OpenDirectionAsync(monster, reachTwo: true);
        await using SocialClient actor = await SocialClient.ConnectAsync(server.Port, server.Token, TileCentreAt(monster.X + dx, monster.Y + dy));
        await actor.JoinAsync(server.Token);
        VitalsState before = await actor.GetVitalsAsync(server.Token);

        (int _, int delayMs) = await actor.Telegraphs.WaitAsync(t => t.MonsterId == monster.MonsterId, server.Token);
        double stepX = ((monster.X + (2 * dx)) * TileSize) + (TileSize / 2);
        double stepY = ((monster.Y + (2 * dy)) * TileSize) + (TileSize / 2);
        await actor.SendMoveAsync(1, (float)stepX, (float)stepY, server.Token);

        // The wind-up clock stands still until the server has applied the move (no packet acknowledges an accepted move, so the server's
        // presence record is read). Then the wind-up ends: the player is two tiles away, so the strike finds no one in reach.
        await WaitUntilAtAsync(stepX, stepY, server.Token);
        actor.Corrections.TryTake(out _).Should().BeFalse("the step out of reach is a legal move, so the server accepts it");
        server.EndWindup(delayMs);
        await Server.SettleAsync(server.Token);
        VitalsState after = await actor.GetVitalsAsync(server.Token);
        after.Hp.Should().Be(before.Hp, "a player who stepped out of reach before the wind-up ended takes no contact damage");
    }

    // The monster with the most room around it: no other monster within the aggro radius, so a hit on the actor can only be this monster's.
    private static async Task<MonsterRow> IsolatedMonsterAsync(Server server)
    {
        MonstersSnapshot monsters = await ProbeMonstersAsync(server);
        return monsters.Monsters
            .OrderByDescending(m => monsters.Monsters
                .Where(o => o.MonsterId != m.MonsterId)
                .Select(o => ChebyshevTiles(o, m))
                .DefaultIfEmpty(int.MaxValue)
                .Min())
            .First();
    }

    // A side of the monster whose tile (and, for the step-out, the tile two beyond it) is open ground on the woods map.
    private static async Task<(int Dx, int Dy)> OpenDirectionAsync(MonsterRow monster, bool reachTwo)
    {
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(GameMaps.Woods, out MapDefinition? woods);
        foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            bool near = !woods!.Grid.IsBlocked(monster.X + dx, monster.Y + dy);
            bool far = !woods.Grid.IsBlocked(monster.X + (2 * dx), monster.Y + (2 * dy));
            if (near && (!reachTwo || far))
            {
                return (dx, dy);
            }
        }

        throw new InvalidOperationException("the monster stands in a pocket with no open side on the woods map");
    }

    private static (string Map, double X, double Y) TileCentreAt(int x, int y)
        => (GameMaps.Woods, (x * TileSize) + (TileSize / 2), (y * TileSize) + (TileSize / 2));

    // The monster's strike lands at the end of the wind-up; the actor polls its vitals until the hit shows (the wait is bounded by the test timeout).
    private static async Task<VitalsState> WaitForHpBelowAsync(SocialClient actor, int hp, CancellationToken ct)
    {
        while (true)
        {
            VitalsState vitals = await actor.GetVitalsAsync(ct);
            if (vitals.Hp < hp)
            {
                return vitals;
            }

            await Task.Delay(50, ct);
        }
    }

    // Reads the server's presence records for the woods until the actor's record stands at the given pixel position (an accepted move has no reply).
    private static async Task WaitUntilAtAsync(double x, double y, CancellationToken ct)
    {
        IMapPresence presence = InstanceManager.Instance.GetExistingInstance<IMapPresence>()
            ?? throw new InvalidOperationException("The map presence is not registered.");
        string key = MapInstance.Shared(GameMaps.Woods).Key;
        List<PlayerPresence> players = new(8);
        while (true)
        {
            players.Clear();
            presence.CopyPlayers(key, players);
            for (int i = 0; i < players.Count; i++)
            {
                PlayerPresence p = players[i];
                lock (p.Gate)
                {
                    if (Math.Abs(p.Motion.X - x) < 0.01 && Math.Abs(p.Motion.Y - y) < 0.01)
                    {
                        return;
                    }
                }
            }

            await Task.Delay(10, ct);
        }
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
        private readonly FakeTimeProvider _clock;
        private readonly CancellationTokenSource _timeout = new(s_timeout);
        private readonly CancellationTokenSource _ai = new();
        private Task? _aiLoop;

        private Server(NetworkApplication app, FakeTimeProvider clock, ushort port)
        {
            _app = app;
            _clock = clock;
            this.Port = port;
        }

        public ushort Port { get; }

        public CancellationToken Token => _timeout.Token;

        /// <summary>
        /// Ends a monster's strike wind-up. The server times the wind-up on the test clock, so it ends only when the test moves that clock on, and
        /// never by the real time a test happens to take (docs/adr/ADR-097 D1: the wind-up lasts <paramref name="delayMs"/> on the server).
        /// </summary>
        public void EndWindup(int delayMs) => _clock.Advance(TimeSpan.FromMilliseconds(delayMs));

        /// <summary>
        /// Lets the wind-up's continuation run after <see cref="EndWindup"/>. The continuation does no I/O when the player is out of reach, so a
        /// short settle is enough for the negative check; the positive check waits on the hit itself.
        /// </summary>
        public static async Task SettleAsync(CancellationToken ct) => await Task.Delay(TimeSpan.FromMilliseconds(250), ct);

        /// <summary>
        /// Starts the server. The monster AI is not part of the application (the host's loops start it), so a test that needs monsters to strike runs
        /// the same AI tick here at the server's own interval (<paramref name="monsterAi"/>, docs/adr/ADR-097).
        /// </summary>
        public static async Task<Server> StartAsync(PostgresFixture database, bool monsterAi = false)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);
            TestAccounts.UseFakeVerifiers(database.Options);
            FakeTimeProvider clock = new(s_start);
            NetworkApplication app = Program.BuildApplication(port, clock, development: true);
            Server server = new(app, clock, port);
            await app.ActivateAsync(server.Token);
            if (monsterAi)
            {
                ICombatService combat = InstanceManager.Instance.GetExistingInstance<ICombatService>()
                    ?? throw new InvalidOperationException("The combat service is not registered.");
                server._aiLoop = combat.RunAiAsync(CombatService.DefaultAiInterval, _ => { }, server._ai.Token);
            }

            return server;
        }

        public async ValueTask DisposeAsync()
        {
            await _ai.CancelAsync();
            if (_aiLoop is not null)
            {
                try
                {
                    await _aiLoop;
                }
                catch (OperationCanceledException)
                {
                }
            }

            _ai.Dispose();
            await _app.DeactivateAsync(CancellationToken.None);
            await _app.DisposeAsync();
            _timeout.Dispose();
        }
    }
}

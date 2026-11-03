// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.S2C;
using Nosum.Wasm.Socket.Combat;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>
/// The farmer's damage numbers (docs/adr/ADR-055 amendment). A monster hit reaches the client twice: in the attack reply and in the vitals push
/// that carries the new HP. Each hit must show once, whichever arrives first, and a heal starts a new run so a later hit is shown.
/// </summary>
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the fake socket owns nothing, pooled packets and channels die with the test.")]
public sealed class CombatHitNumberTests
{
    private const int Hit = 8;
    private const int RedStyle = 2;

    private static VitalsState Vitals(int hp) => new()
    {
        Hp = (ushort)hp,
        MaxHp = 100,
        Mp = 50,
        MaxMp = 50,
    };

    private static AttackMonsterResult HitReply(int hpAfter) => new()
    {
        Status = CombatStatus.Ok,
        MonsterId = 7,
        DamageDealt = 5,
        MonsterHp = 20,
        DamageTaken = Hit,
        Hp = (ushort)hpAfter,
        MaxHp = 100,
        Mp = 50,
        MaxMp = 50,
    };

    private static int FarmerHitNumbers(CombatChannel channel)
    {
        List<FloatingTextClientRow> rows = [];
        channel.CopyFloatingTexts(rows, Environment.TickCount64);
        return rows.Count(row => row.Amount == Hit && row.Style == RedStyle);
    }

    [Fact]
    public void Attack_VitalsPushArrivesBeforeTheReply_ShowsTheHitOnce()
    {
        FakeGameSocket socket = new();
        CombatChannel channel = new(socket);
        socket.Push(Vitals(100));
        socket.Push(Vitals(92));
        socket.ReplyWith(_ => HitReply(92));

        channel.Attack(7, 0);

        FarmerHitNumbers(channel).Should().Be(1);
    }

    [Fact]
    public void Attack_ReplyArrivesBeforeTheVitalsPush_ShowsTheHitOnce()
    {
        FakeGameSocket socket = new();
        CombatChannel channel = new(socket);
        socket.Push(Vitals(100));
        socket.ReplyWith(_ => HitReply(92));

        channel.Attack(7, 0);
        socket.Push(Vitals(92));

        FarmerHitNumbers(channel).Should().Be(1);
    }

    [Fact]
    public void Attack_TwoHitsInARow_ShowBothNumbers()
    {
        FakeGameSocket socket = new();
        CombatChannel channel = new(socket);
        socket.Push(Vitals(100));
        socket.ReplyWith(_ => HitReply(92));
        channel.Attack(7, 0);
        socket.ReplyWith(_ => HitReply(84));

        channel.Attack(7, 0);

        FarmerHitNumbers(channel).Should().Be(2);
    }

    [Fact]
    public void Attack_KillingBlowPushArrivesBeforeTheReply_StillShowsTheNumberOnTheMonster()
    {
        FakeGameSocket socket = new();
        CombatChannel channel = new(socket);
        socket.Push(Vitals(100));
        socket.Push(new MonsterChanged { MonsterId = 7, Kind = 0, X = 10, Y = 12, Hp = 5, MaxHp = 5 });
        socket.ReplyWith(_ =>
        {
            // The zero-HP push is on its way while the attack is in flight: it arrives before the reply.
            socket.Push(new MonsterChanged { MonsterId = 7, Kind = 0, X = 10, Y = 12, Hp = 0, MaxHp = 5 });
            return new AttackMonsterResult
            {
                Status = CombatStatus.Ok,
                MonsterId = 7,
                DamageDealt = 5,
                MonsterHp = 0,
                Hp = 100,
                MaxHp = 100,
                Mp = 50,
                MaxMp = 50,
            };
        });

        channel.Attack(7, 0);

        List<FloatingTextClientRow> rows = [];
        channel.CopyFloatingTexts(rows, Environment.TickCount64);
        rows.Should().ContainSingle(row => row.Amount == 5 && row.WorldX == (10 * 16) + 8 && row.WorldY == (12 * 16) - 6,
            "the killing blow's number stays on the tile the monster died on, though its row was removed before the reply");
    }

    [Fact]
    public void Attack_TheSameHitAfterAHeal_ShowsTheNumberAgain()
    {
        FakeGameSocket socket = new();
        CombatChannel channel = new(socket);
        socket.Push(Vitals(100));
        socket.ReplyWith(_ => HitReply(92));
        channel.Attack(7, 0);
        socket.Push(Vitals(100));

        channel.Attack(7, 0);

        FarmerHitNumbers(channel).Should().Be(2);
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using System.Reflection;
using FluentAssertions;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.C2S;
using Nosum.Contracts.Combat.S2C;
using Xunit;

namespace Nosum.UnitTests.Contracts;

/// <summary>Combat packets (docs/adr/ADR-045): round-trip serialization and opcode band invariants.</summary>
public sealed class CombatPacketTests
{
    [Fact]
    public void C2SPackets_RoundTrip()
    {
        using GetVitals getVitals = new();
        using GetMonsters getMonsters = new();
        using AttackMonster attack = new() { MonsterId = 42, Slot = 8 };
        using DrinkPotion drink = new() { Slot = 5 };
        using CastSkill skill = new() { SkillId = 1, TargetMonsterId = 42 };

        GetVitals.Deserialize(getVitals.Serialize()).Should().NotBeNull();
        GetMonsters.Deserialize(getMonsters.Serialize()).Should().NotBeNull();

        AttackMonster attackCopy = AttackMonster.Deserialize(attack.Serialize());
        (attackCopy.MonsterId, attackCopy.Slot).Should().Be((42, (byte)8));

        DrinkPotion drinkCopy = DrinkPotion.Deserialize(drink.Serialize());
        drinkCopy.Slot.Should().Be(5);

        CastSkill skillCopy = CastSkill.Deserialize(skill.Serialize());
        (skillCopy.SkillId, skillCopy.TargetMonsterId).Should().Be((1, 42));
    }

    [Fact]
    public void S2CPackets_RoundTrip()
    {
        using VitalsState vitals = new()
        {
            Hp = 95,
            MaxHp = 110,
            Mp = 40,
            MaxMp = 55,
            CombatXp = 320,
        };
        VitalsState vitalsCopy = VitalsState.Deserialize(vitals.Serialize());
        (vitalsCopy.Hp, vitalsCopy.MaxHp, vitalsCopy.Mp, vitalsCopy.MaxMp, vitalsCopy.CombatXp)
            .Should().Be(((ushort)95, (ushort)110, (ushort)40, (ushort)55, 320));

        using MonstersSnapshot snapshot = new()
        {
            MapId = "woods",
            Monsters =
            [
                new MonsterRow { MonsterId = 1, Kind = 5, X = 12, Y = 14, Hp = 55, MaxHp = 55, Modifier = 1 },
                new MonsterRow { MonsterId = 2, Kind = 6, X = 18, Y = 20, Hp = 45, MaxHp = 60, Modifier = 0 },
            ],
            Hazards =
            [
                new HazardRow { HazardId = 1, Kind = 1, X = 15, Y = 16, Damage = 4 },
                new HazardRow { HazardId = 2, Kind = 2, X = 20, Y = 22, Damage = 8 },
            ],
        };
        MonstersSnapshot snapshotCopy = MonstersSnapshot.Deserialize(snapshot.Serialize());
        snapshotCopy.MapId.Should().Be("woods");
        snapshotCopy.Monsters.Should().HaveCount(2);
        snapshotCopy.Monsters[0].Should().BeEquivalentTo(new MonsterRow { MonsterId = 1, Kind = 5, X = 12, Y = 14, Hp = 55, MaxHp = 55, Modifier = 1 });
        snapshotCopy.Monsters[1].Should().BeEquivalentTo(new MonsterRow { MonsterId = 2, Kind = 6, X = 18, Y = 20, Hp = 45, MaxHp = 60, Modifier = 0 });
        snapshotCopy.Hazards.Should().HaveCount(2);
        snapshotCopy.Hazards[0].Should().BeEquivalentTo(new HazardRow { HazardId = 1, Kind = 1, X = 15, Y = 16, Damage = 4 });
        snapshotCopy.Hazards[1].Should().BeEquivalentTo(new HazardRow { HazardId = 2, Kind = 2, X = 20, Y = 22, Damage = 8 });

        using MonsterChanged changed = new()
        {
            MonsterId = 7,
            Kind = 1,
            X = 10,
            Y = 11,
            Hp = 20,
            MaxHp = 30,
            Modifier = 3,
        };
        MonsterChanged changedCopy = MonsterChanged.Deserialize(changed.Serialize());
        (changedCopy.MonsterId, changedCopy.Kind, changedCopy.X, changedCopy.Y, changedCopy.Hp, changedCopy.MaxHp, changedCopy.Modifier)
            .Should().Be((7, (byte)1, (ushort)10, (ushort)11, (ushort)20, (ushort)30, (byte)3));

        using AttackMonsterResult attackResult = new()
        {
            Status = CombatStatus.Ok,
            MonsterId = 7,
            DamageDealt = 12,
            MonsterHp = 0,
            DamageTaken = 4,
            Hp = 96,
            MaxHp = 100,
            Mp = 42,
            MaxMp = 50,
            CombatXp = 15,
            KnockedOut = false,
            ComboCount = 5,
        };
        AttackMonsterResult attackResultCopy = AttackMonsterResult.Deserialize(attackResult.Serialize());
        (attackResultCopy.Status, attackResultCopy.MonsterId, attackResultCopy.DamageDealt, attackResultCopy.MonsterHp, attackResultCopy.DamageTaken, attackResultCopy.Hp, attackResultCopy.MaxHp, attackResultCopy.Mp, attackResultCopy.MaxMp, attackResultCopy.CombatXp, attackResultCopy.KnockedOut, attackResultCopy.ComboCount)
            .Should().Be((CombatStatus.Ok, 7, (ushort)12, (ushort)0, (ushort)4, (ushort)96, (ushort)100, (ushort)42, (ushort)50, 15, false, (byte)5));

        using DrinkPotionResult drinkResult = new()
        {
            Status = CombatStatus.Ok,
            Hp = 100,
            MaxHp = 100,
            Mp = 50,
            MaxMp = 50,
        };
        DrinkPotionResult drinkResultCopy = DrinkPotionResult.Deserialize(drinkResult.Serialize());
        (drinkResultCopy.Status, drinkResultCopy.Hp, drinkResultCopy.MaxHp, drinkResultCopy.Mp, drinkResultCopy.MaxMp)
            .Should().Be((CombatStatus.Ok, (ushort)100, (ushort)100, (ushort)50, (ushort)50));

        using CastSkillResult skillResult = new()
        {
            Status = CombatStatus.Ok,
            SkillId = 1,
            DamageDealt = 36,
            TargetsHit = 2,
            HealAmount = 0,
            Hp = 90,
            MaxHp = 100,
            Mp = 35,
            MaxMp = 50,
            CombatXp = 50,
            CooldownMs = 3000,
            ComboCount = 3,
        };
        CastSkillResult skillResultCopy = CastSkillResult.Deserialize(skillResult.Serialize());
        (skillResultCopy.Status, skillResultCopy.SkillId, skillResultCopy.DamageDealt, skillResultCopy.TargetsHit, skillResultCopy.HealAmount, skillResultCopy.Hp, skillResultCopy.MaxHp, skillResultCopy.Mp, skillResultCopy.MaxMp, skillResultCopy.CombatXp, skillResultCopy.CooldownMs, skillResultCopy.ComboCount)
            .Should().Be((CombatStatus.Ok, (byte)1, (ushort)36, (byte)2, (ushort)0, (ushort)90, (ushort)100, (ushort)35, (ushort)50, 50, (ushort)3000, (byte)3));
    }

    [Fact]
    public void CombatOpcodes_AreUnique_AndInsideThe0xC200Band()
    {
        ushort[] opcodes = [.. typeof(CombatOpCode).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (ushort)f.GetRawConstantValue()!)];
        opcodes.Should().OnlyHaveUniqueItems().And.OnlyContain(o => o >= 0xC200 && o <= 0xC2FF);
    }

    [Fact]
    public void MonsterChanged_RoundTrip_PreservesEnragedFlag()
    {
        using MonsterChanged changed = new() { MonsterId = 3, Kind = 9, X = 4, Y = 5, Hp = 50, MaxHp = 180, Enraged = 1 };

        MonsterChanged changedCopy = MonsterChanged.Deserialize(changed.Serialize());
        (changedCopy.MonsterId, changedCopy.Hp, changedCopy.Enraged).Should().Be((3, (ushort)50, (byte)1));
    }

    [Fact]
    public void GroundTelegraph_RoundTrip_PreservesFieldsAndOpCode()
    {
        using GroundTelegraph telegraph = new() { X = 12, Y = 30, RadiusTiles = 1, DelayMs = 1000 };

        GroundTelegraph telegraphCopy = GroundTelegraph.Deserialize(telegraph.Serialize());
        (telegraphCopy.X, telegraphCopy.Y, telegraphCopy.RadiusTiles, telegraphCopy.DelayMs).Should().Be(((ushort)12, (ushort)30, (byte)1, (ushort)1000));
        GroundTelegraph.StaticOpCode.Should().Be(CombatOpCode.GroundTelegraph);
    }

    [Fact]
    public void StrikeTelegraph_RoundTrip_PreservesFieldsAndOpCode()
    {
        using StrikeTelegraph strike = new() { MonsterId = 77, DelayMs = 400 };

        StrikeTelegraph strikeCopy = StrikeTelegraph.Deserialize(strike.Serialize());
        (strikeCopy.MonsterId, strikeCopy.DelayMs).Should().Be((77, (ushort)400));
        StrikeTelegraph.StaticOpCode.Should().Be(0xC249);
        StrikeTelegraph.StaticOpCode.Should().Be(CombatOpCode.StrikeTelegraph);
    }
}

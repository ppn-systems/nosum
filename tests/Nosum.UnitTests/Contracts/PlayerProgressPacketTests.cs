// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Xunit;

namespace Nosum.UnitTests.Contracts;

/// <summary>The energy, skill and eat packets of the Player band (docs/adr/ADR-023 D5): round trips and fixed opcodes.</summary>
public sealed class PlayerProgressPacketTests
{
    [Fact]
    public void Eat_EatResult_RoundTrip()
    {
        using Eat eat = new() { Slot = 4 };
        using EatResult result = new() { Status = EatStatus.EnergyFull };

        Eat.Deserialize(eat.Serialize()).Slot.Should().Be(4);
        EatResult.Deserialize(result.Serialize()).Status.Should().Be(EatStatus.EnergyFull);
    }

    [Fact]
    public void EnergyChanged_SkillXpChanged_LevelUp_RoundTrip()
    {
        using EnergyChanged energy = new() { Energy = 268, MaxEnergy = 270 };
        using SkillXpChanged xp = new() { FarmingXp = 15000, ForagingXp = 7, MiningXp = 123_456, FishingXp = 789 };
        using LevelUp level = new() { Skill = 3, Level = 10 };

        EnergyChanged energyCopy = EnergyChanged.Deserialize(energy.Serialize());
        SkillXpChanged xpCopy = SkillXpChanged.Deserialize(xp.Serialize());
        LevelUp levelCopy = LevelUp.Deserialize(level.Serialize());

        (energyCopy.Energy, energyCopy.MaxEnergy).Should().Be(((ushort)268, (ushort)270));
        (xpCopy.FarmingXp, xpCopy.ForagingXp, xpCopy.MiningXp, xpCopy.FishingXp).Should().Be((15000, 7, 123_456, 789));
        (levelCopy.Skill, levelCopy.Level).Should().Be(((byte)3, (byte)10));
    }

    [Fact]
    public void Opcodes_AreAppendedToThePlayerBand_AndNeverMove()
    {
        Eat.StaticOpCode.Should().Be(0x2002).And.Be(PlayerOpCode.Eat);
        EatResult.StaticOpCode.Should().Be(0x2044);
        EnergyChanged.StaticOpCode.Should().Be(0x2045);
        SkillXpChanged.StaticOpCode.Should().Be(0x2046);
        LevelUp.StaticOpCode.Should().Be(0x2047);
        JoinMap.StaticOpCode.Should().Be(0x2000);
        MoveIntent.StaticOpCode.Should().Be(0x2001);
        MoveCorrection.StaticOpCode.Should().Be(0x2043);
    }
}

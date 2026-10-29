// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.C2S;
using Nosum.Contracts.Combat.S2C;
using Xunit;

namespace Nosum.UnitTests.Contracts;

public sealed class BestiaryPacketTests
{
    [Fact]
    public void BestiaryOpCodes_AreFrozenInTheCombatBand()
    {
        // Append-only (docs/adr/ADR-067): these numbers never change and are never reused.
        CombatOpCode.GetBestiary.Should().Be(0xC205);
        CombatOpCode.EquipBestiaryTitle.Should().Be(0xC206);
        CombatOpCode.BestiarySnapshot.Should().Be(0xC247);
        CombatOpCode.EquipBestiaryTitleResult.Should().Be(0xC248);
    }

    [Fact]
    public void StaticOpCodes_OfTheBestiaryPackets_MatchTheirConstants()
    {
        GetBestiary.StaticOpCode.Should().Be(CombatOpCode.GetBestiary);
        EquipBestiaryTitle.StaticOpCode.Should().Be(CombatOpCode.EquipBestiaryTitle);
        BestiarySnapshot.StaticOpCode.Should().Be(CombatOpCode.BestiarySnapshot);
        EquipBestiaryTitleResult.StaticOpCode.Should().Be(CombatOpCode.EquipBestiaryTitleResult);
    }
}

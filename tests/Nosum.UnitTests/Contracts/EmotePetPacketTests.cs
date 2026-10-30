// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Contracts;
using Nosum.Contracts.Emote;
using Nosum.Contracts.Emote.C2S;
using Nosum.Contracts.Emote.S2C;
using Nosum.Contracts.Look.S2C;
using Nosum.Contracts.Pet;
using Nosum.Contracts.Pet.C2S;
using Nosum.Contracts.Pet.S2C;
using Xunit;

namespace Nosum.UnitTests.Contracts;

/// <summary>The wire values of the emote and pet domains are frozen (docs/adr/ADR-075 D5): a changed opcode breaks every client.</summary>
public sealed class EmotePetPacketTests
{
    [Fact]
    public void EmoteOpCode_Values_AreFrozen()
    {
        EmoteOpCode.SendEmote.Should().Be(0x9400);
        EmoteOpCode.EmoteResult.Should().Be(0x9440);
        EmoteOpCode.EmoteShown.Should().Be(0x9441);
    }

    [Fact]
    public void PetOpCode_Values_AreFrozen()
    {
        PetOpCode.GetPet.Should().Be(0x9500);
        PetOpCode.AdoptPet.Should().Be(0x9501);
        PetOpCode.PetPet.Should().Be(0x9502);
        PetOpCode.PetState.Should().Be(0x9540);
    }

    [Fact]
    public void PacketsCarryTheirOpcode()
    {
        SendEmote.StaticOpCode.Should().Be(EmoteOpCode.SendEmote);
        EmoteResult.StaticOpCode.Should().Be(EmoteOpCode.EmoteResult);
        EmoteShown.StaticOpCode.Should().Be(EmoteOpCode.EmoteShown);
        GetPet.StaticOpCode.Should().Be(PetOpCode.GetPet);
        AdoptPet.StaticOpCode.Should().Be(PetOpCode.AdoptPet);
        PetPet.StaticOpCode.Should().Be(PetOpCode.PetPet);
        PetState.StaticOpCode.Should().Be(PetOpCode.PetState);
    }

    [Fact]
    public void NosumProtocol_Version_IsAtLeastEighteen_WithTheEmoteAndPetChange()
    {
        NosumProtocol.Version.Should().BeGreaterThanOrEqualTo(18);
    }

    [Fact]
    public void LookRow_PetFields_AreAppendedAfterTheNickname()
    {
        LookRow row = LookRow.Create();
        row.PetKind = 2;
        row.PetName = "Rex";

        row.Nickname.Should().BeEmpty("the new fields do not disturb the earlier ones");
        row.PetKind.Should().Be(2);
        row.PetName.Should().Be("Rex");
    }
}

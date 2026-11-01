// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Contracts;
using Nosum.Contracts.Dev;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;
using Xunit;

namespace Nosum.UnitTests.Contracts;

/// <summary>The Development QA friend packets (docs/adr/ADR-097): their opcodes sit in the tester band and the protocol version moved with them.</summary>
public sealed class DevBefriendPacketTests
{
    [Fact]
    public void NosumProtocol_Version_IsThirtyTwo_WithTheDevelopmentQaFriend()
    {
        NosumProtocol.Version.Should().Be(32);
    }

    [Fact]
    public void DevBefriend_Opcodes_AreTheNextFreeTesterValues()
    {
        DevBefriend.StaticOpCode.Should().Be(0xF006);
        DevBefriendResult.StaticOpCode.Should().Be(0xF046);
        DevOpCode.DevBefriend.Should().Be(0xF006);
        DevOpCode.DevBefriendResult.Should().Be(0xF046);
    }

    [Fact]
    public void DevBefriend_Opcode_DoesNotCollideWithTheOtherTesterRequests()
    {
        ushort[] requests = [DevOpCode.DevGrant, DevOpCode.DevAdvanceClock, DevOpCode.DevTeleport, DevOpCode.DevSeason, DevOpCode.DevLevelUp, DevOpCode.DevFishingBite];
        requests.Should().NotContain(DevOpCode.DevBefriend);
    }
}

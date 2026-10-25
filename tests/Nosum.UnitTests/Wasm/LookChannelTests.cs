// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Contracts.Look.S2C;
using Nosum.Contracts.Player.S2C;
using Nosum.Wasm.Socket.Look;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class LookChannelTests
{
    private static void PushLook(FakeGameSocket socket, uint playerId, int skin, string nickname)
    {
        using LookChanged packet = new() { Look = new LookRow { PlayerId = playerId, SkinId = (byte)skin, Nickname = nickname } };
        socket.Push(packet);
    }

    [Fact]
    public void PlayerLeft_ForgetsThatPlayersLookAndNickname_AndKeepsTheOthers()
    {
        FakeGameSocket socket = new();
        using LookChannel channel = new(socket, () => 1);
        PushLook(socket, 1, 2, "Mai");
        PushLook(socket, 7, 3, "Lan");
        int changed = 0;
        channel.Changed += () => changed++;
        PlayerLeft left = new() { PlayerId = 7 };

        socket.Push(left);

        channel.TryGet(7, out _).Should().BeFalse();
        channel.TryGetNickname(7, out _).Should().BeFalse();
        channel.TryGet(1, out LookInfo mine).Should().BeTrue();
        mine.Nickname.Should().Be("Mai");
        changed.Should().Be(1);
        left.Dispose();
    }

    [Fact]
    public void LookChanged_AfterAPlayerCameBackIntoSight_StoresTheLookAgain()
    {
        FakeGameSocket socket = new();
        using LookChannel channel = new(socket, () => 1);
        PushLook(socket, 7, 3, "Lan");
        PlayerLeft left = new() { PlayerId = 7 };
        socket.Push(left);

        PushLook(socket, 7, 4, "Lan");

        channel.TryGet(7, out LookInfo look).Should().BeTrue();
        look.SkinId.Should().Be(4);
        left.Dispose();
    }
}

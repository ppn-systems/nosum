// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Nosum.Contracts.Emote;
using Nosum.Contracts.Emote.C2S;
using Nosum.Contracts.Emote.S2C;
using Nosum.Gameplay.Emotes;
using Nosum.Wasm.Socket.Emote;
using Nosum.Wasm.ViewModels.Emotes;
using Nosum.Wasm.ViewModels.Settings;
using Xunit;

namespace Nosum.UnitTests.Wasm;

[SuppressMessage("Reliability", "CA2000", Justification = "Test fixtures: the fake socket owns nothing, and the view model disposes the channel it is given.")]
public sealed class EmoteWheelViewModelTests
{
    private const long Now = 1_000;

    private static EmoteWheelViewModel Build(FakeGameSocket socket, EmoteBubbleStore bubbles, uint localId)
    {
        EmoteChannel channel = new(socket);
        return new EmoteWheelViewModel(channel, bubbles, () => localId, WasmTestSupport.Text(), () => Now);
    }

    [Fact]
    public void Options_HoldTheTwelveEmotesInCodeOrder()
    {
        using EmoteWheelViewModel vm = Build(new FakeGameSocket(), new EmoteBubbleStore(), 5);

        vm.Options.Should().HaveCount(EmoteRules.Count);
        vm.Options[3].Code.Should().Be(3);
        vm.Options[3].Icon.Should().Be("emote-3");
    }

    [Fact]
    public void Toggle_TwiceFromClosed_ReturnsToClosed()
    {
        using EmoteWheelViewModel vm = Build(new FakeGameSocket(), new EmoteBubbleStore(), 5);

        vm.Toggle();
        bool opened = vm.IsOpen;
        vm.Toggle();

        opened.Should().BeTrue();
        vm.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Choose_ServerAcceptsTheEmote_SendsCodeAndShowsOwnBubble()
    {
        FakeGameSocket socket = new();
        EmoteBubbleStore bubbles = new();
        byte sent = byte.MaxValue;
        socket.ReplyWith<EmoteResult>(request =>
        {
            sent = ((SendEmote)request).Emote;
            return new EmoteResult { Status = EmoteStatus.Ok };
        });
        using EmoteWheelViewModel vm = Build(socket, bubbles, 5);
        vm.Toggle();

        vm.Choose(2);

        sent.Should().Be(2);
        vm.IsOpen.Should().BeFalse();
        bubbles.TryGet(5, Now, out byte shown).Should().BeTrue();
        shown.Should().Be(2);
    }

    [Fact]
    public void Choose_Cooldown_ShowsTheMessageAndNoBubble()
    {
        FakeGameSocket socket = new();
        EmoteBubbleStore bubbles = new();
        socket.ReplyWith<EmoteResult>(_ => new EmoteResult { Status = EmoteStatus.Cooldown });
        using EmoteWheelViewModel vm = Build(socket, bubbles, 5);

        vm.Choose(2);

        vm.Notice.Should().Be(WasmTestSupport.Text()["Emote_Cooldown"].Value);
        bubbles.TryGet(5, Now, out _).Should().BeFalse();
    }

    [Fact]
    public void RemoteEmoteShown_ShowsTheBubbleAboveThatFarmer()
    {
        FakeGameSocket socket = new();
        EmoteBubbleStore bubbles = new();
        using EmoteWheelViewModel vm = Build(socket, bubbles, 5);

        using (EmoteShown push = new() { PlayerId = 9, Emote = 4 })
        {
            socket.Push(push);
        }

        bubbles.TryGet(9, Now, out byte shown).Should().BeTrue();
        shown.Should().Be(4);
    }

    [Fact]
    public void EmoteHotkey_IsReservedSoNoActionTakesIt()
    {
        KeyBindings.IsReserved(EmoteWheelViewModel.Hotkey).Should().BeTrue();
    }
}

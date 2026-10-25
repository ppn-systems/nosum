// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Chat;
using Nosum.Contracts.Chat.C2S;
using Nosum.Contracts.Chat.S2C;
using Nosum.Wasm.Socket.Chat;
using Nosum.Wasm.ViewModels.Chat;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

[SuppressMessage("Reliability", "CA2000", Justification = "Packets and channels here are plain objects over a fake socket that dies with the test; nothing is rented from a pool.")]
public sealed class ChatTests
{
    private static ChatMessage Message(ChatChannel channel, string from, string text, string to = "")
        => new() { Channel = channel, FromNickname = from, ToNickname = to, Text = text };

    [Theory]
    [InlineData("hello", ChatCommandKind.Say, ChatChannel.Map, "", "hello")]
    [InlineData("  hello  ", ChatCommandKind.Say, ChatChannel.Map, "", "hello")]
    [InlineData("", ChatCommandKind.None, ChatChannel.Map, "", "")]
    [InlineData("/w Lan hi there", ChatCommandKind.Whisper, ChatChannel.Whisper, "Lan", "hi there")]
    [InlineData("/whisper Lan   hi", ChatCommandKind.Whisper, ChatChannel.Whisper, "Lan", "hi")]
    [InlineData("/W Lan hi", ChatCommandKind.Whisper, ChatChannel.Whisper, "Lan", "hi")]
    [InlineData("/w Guest 0042 hello", ChatCommandKind.Whisper, ChatChannel.Whisper, "guest-0042", "hello")]
    [InlineData("/w khách 42 hello", ChatCommandKind.Whisper, ChatChannel.Whisper, "guest-42", "hello")]
    [InlineData("/w Lan", ChatCommandKind.Usage, ChatChannel.Whisper, "Lan", "")]
    [InlineData("/w", ChatCommandKind.Usage, ChatChannel.Whisper, "", "")]
    [InlineData("/l", ChatCommandKind.Switch, ChatChannel.Local, "", "")]
    [InlineData("/m", ChatCommandKind.Switch, ChatChannel.Map, "", "")]
    [InlineData("/c", ChatCommandKind.Switch, ChatChannel.Coop, "", "")]
    [InlineData("/c everyone", ChatCommandKind.Say, ChatChannel.Coop, "", "everyone")]
    [InlineData("/block Lan", ChatCommandKind.Block, ChatChannel.Map, "Lan", "")]
    [InlineData("/unblock Lan", ChatCommandKind.Unblock, ChatChannel.Map, "Lan", "")]
    [InlineData("/report Guest 7", ChatCommandKind.Report, ChatChannel.Map, "guest-7", "")]
    [InlineData("/block", ChatCommandKind.Usage, ChatChannel.Map, "", "")]
    [InlineData("/dance", ChatCommandKind.Unknown, ChatChannel.Map, "", "/dance")]
    public void Parse_TypedLine_GivesTheRequestedCommand(string input, ChatCommandKind kind, ChatChannel channel, string target, string text)
    {
        ChatCommand command = ChatCommand.Parse(input, ChatChannel.Map);

        command.Should().Be(new ChatCommand(kind, channel, target, text));
    }

    [Fact]
    public void VisibleLines_Collapsed_ShowsOnlyTheLastThreeUnfadedLines()
    {
        long now = 1_000_000;
        FakeGameSocket socket = new();
        using ChatViewModel vm = Create(socket, () => now);
        foreach (string text in new[] { "one", "two", "three", "four" })
        {
            socket.Push(Message(ChatChannel.Local, "Lan", text));
            now += 1000;
        }

        IReadOnlyList<ChatLineView> lines = vm.VisibleLines(now);

        lines.Select(l => l.Text).Should().Equal("two", "three", "four");
    }

    [Fact]
    public void VisibleLines_LinesOlderThanTheFadeTime_AreGone()
    {
        long now = 1_000_000;
        FakeGameSocket socket = new();
        using ChatViewModel vm = Create(socket, () => now);
        socket.Push(Message(ChatChannel.Local, "Lan", "old"));
        now += 5000;
        socket.Push(Message(ChatChannel.Local, "Lan", "new"));

        vm.VisibleLines(now + ChatViewModel.FadeMs - 5001).Select(l => l.Text).Should().Equal("old", "new");
        vm.VisibleLines(now + ChatViewModel.FadeMs - 4999).Select(l => l.Text).Should().Equal("new");
        vm.VisibleLines(now + ChatViewModel.FadeMs + 1).Should().BeEmpty();
    }

    [Fact]
    public void VisibleLines_InputOpen_ShowsEveryKeptLineEvenIfFaded()
    {
        long now = 1_000_000;
        FakeGameSocket socket = new();
        using ChatViewModel vm = Create(socket, () => now);
        for (int i = 0; i < 6; i++)
        {
            socket.Push(Message(ChatChannel.Local, "Lan", "line " + i));
        }

        vm.OpenInput();

        vm.VisibleLines(now + 60_000).Should().HaveCount(6);
    }

    [Fact]
    public void Lines_MoreThanTheRingHolds_KeepsTheNewestHundred()
    {
        FakeGameSocket socket = new();
        using ChatViewModel vm = Create(socket, () => 0);
        for (int i = 0; i < ChatViewModel.MaxLines + 25; i++)
        {
            socket.Push(Message(ChatChannel.Local, "Lan", "line " + i));
        }
        vm.OpenInput();

        ChatLineView[] lines = vm.VisibleLines(0);

        lines.Should().HaveCount(ChatViewModel.MaxLines);
        lines[0].Text.Should().Be("line 25");
        lines[^1].Text.Should().Be("line 124");
    }

    [Fact]
    public void Tick_RaisesChangedOnlyWhenTheFadedPictureChanges()
    {
        long now = 1_000_000;
        FakeGameSocket socket = new();
        using ChatViewModel vm = Create(socket, () => now);
        socket.Push(Message(ChatChannel.Local, "Lan", "hi"));
        int changed = 0;
        vm.Changed += () => changed++;

        now += 2000;
        vm.Tick();
        int whileVisible = changed;
        now += ChatViewModel.FadeMs;
        vm.Tick();
        int afterFade = changed;
        vm.Tick();

        whileVisible.Should().Be(0);
        afterFade.Should().Be(1);
        changed.Should().Be(1);
    }

    [Fact]
    public void PlayerText_IsKeptVerbatimAsAPlainString()
    {
        FakeGameSocket socket = new();
        using ChatViewModel vm = Create(socket, () => 0);
        const string Hostile = "<b onclick=\"x()\">hi</b> & <script>alert(1)</script>";

        socket.Push(Message(ChatChannel.Local, "Lan", Hostile));

        vm.VisibleLines(0).Single().Text.Should().Be(Hostile);
    }

    [Fact]
    public void VisibleLines_GuestSender_ShowsTheLocalizedGuestName_AndWhisperShowsBothNames()
    {
        FakeGameSocket socket = new();
        using ChatViewModel vm = Create(socket, () => 0);

        socket.Push(Message(ChatChannel.Whisper, "guest-0042", "psst", "Lan"));
        ChatLineView line = vm.VisibleLines(0).Single();

        line.Kind.Should().Be(ChatLineKind.Whisper);
        line.Sender.Should().Contain("0042").And.Contain("Lan").And.NotContain("guest-");
        line.Nickname.Should().Be("guest-0042");
    }

    [Fact]
    public void Submit_PlainLine_SendsOnTheSelectedChannelAndClosesTheBox()
    {
        FakeGameSocket socket = new();
        List<SendChat> sent = ReplyOk(socket);
        using ChatViewModel vm = Create(socket, () => 0);
        vm.OpenInput();
        vm.CycleChannel();

        vm.Submit("hello");

        sent.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Channel = ChatChannel.Map, Target = "", Text = "hello" });
        vm.InputOpen.Should().BeFalse();
    }

    [Fact]
    public void Submit_WhisperCommand_SendsAWhisperToTheNickname()
    {
        FakeGameSocket socket = new();
        List<SendChat> sent = ReplyOk(socket);
        using ChatViewModel vm = Create(socket, () => 0);

        vm.Submit("/w Lan see you");

        sent.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Channel = ChatChannel.Whisper, Target = "Lan", Text = "see you" });
    }

    [Fact]
    public void Submit_ChannelCommand_SwitchesAndKeepsTheBoxOpen()
    {
        FakeGameSocket socket = new();
        List<SendChat> sent = ReplyOk(socket);
        using ChatViewModel vm = Create(socket, () => 0);
        vm.OpenInput();

        vm.Submit("/c");

        vm.Channel.Should().Be(ChatChannel.Coop);
        vm.InputOpen.Should().BeTrue();
        sent.Should().BeEmpty();
    }

    [Fact]
    public void CycleChannel_WalksLocalMapCoopAndBack_AGuestStaysLocal()
    {
        FakeGameSocket socket = new();
        bool guest = false;
        using ChatViewModel vm = Create(socket, () => 0, () => guest);

        List<ChatChannel> walk = [];
        for (int i = 0; i < 4; i++)
        {
            vm.CycleChannel();
            walk.Add(vm.Channel);
        }

        guest = true;
        vm.CycleChannel();

        walk.Should().Equal(ChatChannel.Map, ChatChannel.Coop, ChatChannel.Local, ChatChannel.Map);
        vm.Channel.Should().Be(ChatChannel.Map, "a guest does not move the channel");
    }

    [Fact]
    public void Submit_GuestOnAnotherChannel_ShowsTheLocalOnlyLineWithoutSending()
    {
        FakeGameSocket socket = new();
        List<SendChat> sent = ReplyOk(socket);
        using ChatViewModel vm = Create(socket, () => 0, () => true);

        vm.Submit("/m hello");

        sent.Should().BeEmpty();
        ChatLineView line = vm.VisibleLines(0).Single();
        line.Kind.Should().Be(ChatLineKind.Error);
        line.Text.Should().NotBeNullOrEmpty().And.NotStartWith("Chat_");
    }

    [Fact]
    public void Submit_RefusedByTheServer_AddsALocalizedSystemLine()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new ChatResult { Status = ChatStatus.RateLimited });
        using ChatViewModel vm = Create(socket, () => 0);

        vm.Submit("hello");

        ChatLineView line = vm.VisibleLines(0).Single();
        line.Kind.Should().Be(ChatLineKind.Error);
        line.Text.Should().Be(SocialClientFixtures.Localizer<Language>()["Chat_Status_RateLimited"].Value);
    }

    [Fact]
    public void Submit_UnknownCommand_KeepsTheBoxAndTheTextAndExplains()
    {
        FakeGameSocket socket = new();
        List<SendChat> sent = ReplyOk(socket);
        using ChatViewModel vm = Create(socket, () => 0);
        vm.OpenInput();

        vm.Submit("/dance");

        sent.Should().BeEmpty();
        vm.InputOpen.Should().BeTrue();
        vm.Draft.Should().Be("/dance");
        vm.VisibleLines(0).Single().Kind.Should().Be(ChatLineKind.Error);
    }

    [Fact]
    public void Submit_BlockCommand_SendsTheRequestAndConfirmsInTheLog()
    {
        FakeGameSocket socket = new();
        List<string> blocks = [];
        socket.ReplyWith(request =>
        {
            blocks.Add(((BlockPlayer)request).Nickname);
            return new ChatResult { Status = ChatStatus.Ok };
        });
        socket.ReplyWith(_ => new BlockedList());
        using ChatViewModel vm = Create(socket, () => 0);

        vm.Submit("/block Lan");

        blocks.Should().Equal("Lan");
        ChatLineView line = vm.VisibleLines(0).Single();
        line.Kind.Should().Be(ChatLineKind.System);
        line.Text.Should().Contain("Lan");
    }

    [Fact]
    public void StartWhisper_OpensTheBoxWithTheWhisperPrefix()
    {
        FakeGameSocket socket = new();
        using ChatViewModel vm = Create(socket, () => 0);

        vm.StartWhisper("guest-0042");

        vm.InputOpen.Should().BeTrue();
        vm.Draft.Should().Be("/w guest-0042 ");
    }

    [Fact]
    public void ChannelLabel_FollowsTheUiLanguage()
    {
        FakeGameSocket socket = new();
        using ChatViewModel vm = Create(socket, () => 0);

        using (new SocialClientFixtures.CultureScope("en"))
        {
            vm.ChannelLabel.Should().Be("Local");
        }

        using (new SocialClientFixtures.CultureScope("vi"))
        {
            vm.ChannelLabel.Should().Be("Gần");
        }
    }

    [Fact]
    public void Language_EveryChatStatusAndChannelHasATextInBothLanguages()
    {
        List<string> needed = [];
        needed.AddRange(System.Enum.GetValues<ChatStatus>().Where(s => s != ChatStatus.Ok).Select(s => "Chat_Status_" + s));
        needed.AddRange(System.Enum.GetValues<ChatChannel>().Select(c => "Chat_Channel_" + c));
        needed.AddRange(["Chat_InputHint", "Chat_GuestHint", "Chat_Sys_Usage", "Chat_Sys_UnknownCommand", "Chat_Sys_Blocked", "Chat_Sys_Unblocked", "Chat_Sys_Reported"]);

        SocialClientFixtures.Keys("Language.resx").Should().Contain(needed);
        SocialClientFixtures.Keys("Language.en.resx").Should().Contain(needed);
    }

    private static List<SendChat> ReplyOk(FakeGameSocket socket)
    {
        List<SendChat> sent = [];
        socket.ReplyWith(request =>
        {
            SendChat packet = (SendChat)request;
            sent.Add(new SendChat { Channel = packet.Channel, Target = packet.Target, Text = packet.Text });
            return new ChatResult { Status = ChatStatus.Ok };
        });
        return sent;
    }

    private static ChatViewModel Create(FakeGameSocket socket, System.Func<long> clock, System.Func<bool>? isGuest = null)
        => new(new ChatMessenger(socket), SocialClientFixtures.Names(socket), SocialClientFixtures.Localizer<Language>(), isGuest, clock, ticks: false);
}

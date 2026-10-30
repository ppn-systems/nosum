// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Emotes;
using Nosum.Wasm.ViewModels.Emotes;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class EmoteBubbleStoreTests
{
    [Fact]
    public void TryGet_WithinTheShownWindow_ReturnsTheEmote()
    {
        EmoteBubbleStore store = new();
        store.Show(7, 3, 1_000);

        bool found = store.TryGet(7, 1_000 + EmoteRules.ShownMs - 1, out byte emote);

        found.Should().BeTrue();
        emote.Should().Be(3);
    }

    [Fact]
    public void TryGet_AfterTheShownWindow_ReturnsFalse()
    {
        EmoteBubbleStore store = new();
        store.Show(7, 3, 1_000);

        bool found = store.TryGet(7, 1_000 + EmoteRules.ShownMs, out _);

        found.Should().BeFalse();
    }

    [Fact]
    public void Show_ZeroPlayerId_IsIgnored()
    {
        EmoteBubbleStore store = new();

        store.Show(0, 3, 1_000);

        store.TryGet(0, 1_000, out _).Should().BeFalse();
    }

    [Fact]
    public void Show_CodeOffTheWheel_IsIgnored()
    {
        EmoteBubbleStore store = new();

        store.Show(7, (byte)EmoteRules.Count, 1_000);

        store.TryGet(7, 1_000, out _).Should().BeFalse();
    }

    [Fact]
    public void Show_SamePlayerTwice_ShowsTheNewerEmote()
    {
        EmoteBubbleStore store = new();
        store.Show(7, 1, 0);

        store.Show(7, 2, 100);

        store.TryGet(7, 150, out byte emote).Should().BeTrue();
        emote.Should().Be(2);
    }

    [Fact]
    public void Show_WhenFull_ReplacesTheBubbleThatExpiresFirst()
    {
        EmoteBubbleStore store = new();
        for (int i = 0; i < EmoteBubbleStore.Capacity; i++)
        {
            store.Show((uint)(i + 1), 0, i);
        }

        store.Show(999, 5, 100);

        store.TryGet(1, 100, out _).Should().BeFalse();
        store.TryGet(2, 100, out _).Should().BeTrue();
        store.TryGet(999, 100, out byte emote).Should().BeTrue();
        emote.Should().Be(5);
    }
}

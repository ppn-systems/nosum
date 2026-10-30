// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Emotes;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

public sealed class EmoteRulesTests
{
    [Fact]
    public void IsValid_TheTwelveCodesOnTheWheel_AreValid()
    {
        for (byte code = 0; code < EmoteRules.Count; code++)
        {
            EmoteRules.IsValid(code).Should().BeTrue($"code {code} is on the wheel");
        }
    }

    [Fact]
    public void IsValid_TheFirstCodeOffTheWheel_IsRefused()
    {
        EmoteRules.IsValid((byte)EmoteRules.Count).Should().BeFalse();
    }

    [Fact]
    public void EmoteCooldown_FirstEmote_PassesAtOnce()
    {
        EmoteCooldown cooldown = new();

        cooldown.TryTake(0).Should().BeTrue();
    }

    [Fact]
    public void EmoteCooldown_InsideTheInterval_IsRefused_AndAfterItPasses()
    {
        EmoteCooldown cooldown = new();
        _ = cooldown.TryTake(10_000);

        cooldown.TryTake(10_000 + EmoteRules.MinIntervalMs - 1).Should().BeFalse("the cooldown has not run out yet");
        cooldown.TryTake(10_000 + EmoteRules.MinIntervalMs).Should().BeTrue("the cooldown has run out");
    }

    [Fact]
    public void EmoteCooldown_ARefusedTry_DoesNotRestartTheCooldown()
    {
        EmoteCooldown cooldown = new();
        _ = cooldown.TryTake(0);
        _ = cooldown.TryTake(500);

        cooldown.TryTake(EmoteRules.MinIntervalMs).Should().BeTrue("the refused try at 500 ms did not start a new interval");
    }
}

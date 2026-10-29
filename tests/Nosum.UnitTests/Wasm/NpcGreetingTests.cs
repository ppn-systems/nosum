// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Npcs;
using Nosum.Gameplay.Weather;
using Nosum.Wasm.ViewModels.Npc;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>A merchant's greeting follows the weather of the day, then the season, then falls back to the plain greeting (issue #130).</summary>
public sealed class NpcGreetingTests
{
    private static readonly NpcDefinition s_seed = NpcCatalog.TryGet("seed_keeper", out NpcDefinition? npc) ? npc! : throw new InvalidOperationException();

    private static readonly HashSet<string> s_all =
    [
        "Npc_seed_keeper_Greeting",
        "Npc_seed_keeper_Greeting_Spring",
        "Npc_seed_keeper_Greeting_Rain",
        "Npc_seed_keeper_Greeting_Snow",
    ];

    [Theory]
    [InlineData(WeatherKind.Rain)]
    [InlineData(WeatherKind.Storm)]
    public void KeyFor_RainOrStorm_UsesTheRainGreeting(WeatherKind weather)
    {
        NpcGreeting.KeyFor(s_seed, weather, Season.Spring, s_all.Contains).Should().Be("Npc_seed_keeper_Greeting_Rain");
    }

    [Fact]
    public void KeyFor_Snow_UsesTheSnowGreetingEvenInWinter()
    {
        NpcGreeting.KeyFor(s_seed, WeatherKind.Snow, Season.Winter, s_all.Contains).Should().Be("Npc_seed_keeper_Greeting_Snow");
    }

    [Fact]
    public void KeyFor_SunnySpring_UsesTheSeasonGreeting()
    {
        NpcGreeting.KeyFor(s_seed, WeatherKind.Sun, Season.Spring, s_all.Contains).Should().Be("Npc_seed_keeper_Greeting_Spring");
    }

    [Fact]
    public void KeyFor_SunnySummerWithNoSummerLine_UsesThePlainGreeting()
    {
        NpcGreeting.KeyFor(s_seed, WeatherKind.Sun, Season.Summer, s_all.Contains).Should().Be("Npc_seed_keeper_Greeting");
    }

    [Fact]
    public void KeyFor_RainInSummer_UsesTheRainGreetingBecauseWeatherComesFirst()
    {
        NpcGreeting.KeyFor(s_seed, WeatherKind.Rain, Season.Summer, s_all.Contains).Should().Be("Npc_seed_keeper_Greeting_Rain");
    }

    [Fact]
    public void KeyFor_WeatherLineMissing_FallsBackToTheSeasonLine()
    {
        HashSet<string> keys = ["Npc_seed_keeper_Greeting", "Npc_seed_keeper_Greeting_Spring"];

        NpcGreeting.KeyFor(s_seed, WeatherKind.Rain, Season.Spring, keys.Contains).Should().Be("Npc_seed_keeper_Greeting_Spring");
    }

    [Fact]
    public void KeyFor_CalendarNotKnown_UsesThePlainGreeting()
    {
        NpcGreeting.KeyFor(s_seed, null, null, s_all.Contains).Should().Be("Npc_seed_keeper_Greeting");
    }

    [Fact]
    public void KeyFor_MerchantWithNoVariants_UsesThePlainGreeting()
    {
        NpcDefinition blacksmith = NpcCatalog.TryGet("blacksmith", out NpcDefinition? npc) ? npc! : throw new InvalidOperationException();

        NpcGreeting.KeyFor(blacksmith, WeatherKind.Rain, Season.Spring, _ => false).Should().Be("Npc_blacksmith_Greeting");
    }
}

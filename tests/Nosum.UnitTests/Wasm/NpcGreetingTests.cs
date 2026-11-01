// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Npcs;
using Nosum.Gameplay.Weather;
using Nosum.Wasm.ViewModels.Npc;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>
/// A merchant's line is chosen from a pool by the day, the season, the weather, the time of day and the farmer's progress (docs/adr/ADR-080 D2,
/// extending the greeting of issue #130). These tests pin the pool's size and text, the fit rule, and the determinism of the choice.
/// </summary>
public sealed class NpcGreetingTests
{
    private static readonly string[] s_merchants = ["seed_keeper", "tailor", "furnisher", "blacksmith", "carpenter", "trader", "board_keeper"];

    private static readonly Season[] s_seasons = [Season.Spring, Season.Summer, Season.Fall, Season.Winter];

    private static readonly WeatherKind[] s_weather = [WeatherKind.Sun, WeatherKind.Rain, WeatherKind.Storm, WeatherKind.Snow];

    private static readonly int[] s_hours = [6, 13, 20];

    private static readonly NpcDefinition s_seed = Merchant("seed_keeper");

    private static NpcDefinition Merchant(string id) => NpcCatalog.TryGet(id, out NpcDefinition? npc) ? npc! : throw new InvalidOperationException(id);

    private static string KeyOf(NpcDefinition npc, NpcLine line) => npc.TextKey("Line_" + line.Number.ToString("00", System.Globalization.CultureInfo.InvariantCulture));

    private static NpcLineContext Moment(long day, Season season, WeatherKind weather, int hour, bool newcomer = false, bool harvested = false, bool deep = false)
        => NpcLineContext.Of(day, season, weather, hour, newcomer, harvested, deep);

    // Every progress combination a farmer can be in.
    private static IEnumerable<(bool Newcomer, bool Harvested, bool Deep)> Progress()
    {
        yield return (true, false, false);
        yield return (false, false, false);
        yield return (false, true, false);
        yield return (false, true, true);
        yield return (false, false, true);
    }

    [Theory]
    [MemberData(nameof(Merchants))]
    public void Pool_HasTwelveToTwentyLines_EachWithTextInBothLanguages(string id)
    {
        NpcDefinition npc = Merchant(id);
        IReadOnlyList<NpcLine> pool = NpcLineCatalog.PoolOf(id);
        HashSet<string> vi = WasmTestSupport.Keys("Language.resx");
        HashSet<string> en = WasmTestSupport.Keys("Language.en.resx");

        pool.Count.Should().BeInRange(12, 20);
        foreach (NpcLine line in pool)
        {
            vi.Should().Contain(KeyOf(npc, line));
            en.Should().Contain(KeyOf(npc, line));
        }

        pool.Select(line => line.Number).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [MemberData(nameof(Merchants))]
    public void Pool_HasUntaggedLines_SoThereIsAlwaysSomethingToSay(string id)
    {
        NpcLineCatalog.PoolOf(id).Count(line => line.Tags == NpcLineTags.None).Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void KeyFor_TheSameDayAndMoment_GivesTheSameLine()
    {
        NpcLineContext moment = Moment(12, Season.Spring, WeatherKind.Sun, 9, newcomer: true);

        NpcGreeting.KeyFor(s_seed, moment, 0, _ => true).Should().Be(NpcGreeting.KeyFor(s_seed, moment, 0, _ => true));
    }

    [Fact]
    public void KeyFor_ChangesFromOneDayToTheNext()
    {
        HashSet<string> seen = [];
        for (long day = 1; day <= GameCalendar.DaysPerSeason; day++)
        {
            seen.Add(NpcGreeting.KeyFor(s_seed, Moment(day, Season.Summer, WeatherKind.Sun, 13), 0, _ => true));
        }

        seen.Count.Should().BeGreaterThanOrEqualTo(3, "a merchant does not say the same line for a whole season");
    }

    [Fact]
    public void KeyFor_EachNextSlot_IsTheNextLineThatFits_AndTheConversationGoesRound()
    {
        NpcLineContext moment = Moment(7, Season.Fall, WeatherKind.Rain, 19, harvested: true);
        List<string> fitting = NpcLineCatalog.PoolOf(s_seed.Id)
            .Where(line => NpcGreeting.Fits(line.Tags, moment.Tags))
            .Select(line => KeyOf(s_seed, line))
            .ToList();

        List<string> spoken = [];
        for (int slot = 0; slot <= fitting.Count; slot++)
        {
            spoken.Add(NpcGreeting.KeyFor(s_seed, moment, slot, _ => true));
        }

        spoken.Take(fitting.Count).Should().OnlyHaveUniqueItems("every line that fits is said once before any repeats");
        spoken.Take(fitting.Count).Should().BeEquivalentTo(fitting);
        spoken[fitting.Count].Should().Be(spoken[0], "after the last one the conversation starts over");
    }

    [Fact]
    public void KeyFor_OnlyEverChoosesALineWhoseMomentIsNow()
    {
        foreach (string id in s_merchants)
        {
            NpcDefinition npc = Merchant(id);
            foreach (Season season in s_seasons)
            {
                foreach (WeatherKind weather in s_weather)
                {
                    foreach (int hour in s_hours)
                    {
                        foreach ((bool newcomer, bool harvested, bool deep) in Progress())
                        {
                            NpcLineContext moment = Moment(3, season, weather, hour, newcomer, harvested, deep);
                            for (int slot = 0; slot < 4; slot++)
                            {
                                string key = NpcGreeting.KeyFor(npc, moment, slot, _ => true);
                                NpcLine line = NpcLineCatalog.PoolOf(id).Single(candidate => KeyOf(npc, candidate) == key);
                                NpcGreeting.Fits(line.Tags, moment.Tags).Should().BeTrue($"{key} was said in {season}, {weather}, hour {hour}");
                            }
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void KeyFor_WithTheCalendarUnknown_OnlyUntaggedAndProgressLinesFit()
    {
        NpcLineContext unknown = NpcLineContext.Of(0, null, null, null, newcomer: true, harvested: false, deepMiner: false);

        for (int slot = 0; slot < 8; slot++)
        {
            string key = NpcGreeting.KeyFor(s_seed, unknown, slot, _ => true);
            NpcLine line = NpcLineCatalog.PoolOf(s_seed.Id).Single(candidate => KeyOf(s_seed, candidate) == key);
            (line.Tags & (NpcLineTags.SeasonGroup | NpcLineTags.WeatherGroup | NpcLineTags.TimeGroup)).Should().Be(NpcLineTags.None, key);
        }
    }

    [Fact]
    public void KeyFor_TheActiveQuestIsAtThisMerchant_TheGreetingNamesIt()
    {
        NpcLineContext moment = NpcLineContext.Of(3, Season.Spring, WeatherKind.Sun, 9, false, false, false, "buy_seeds");

        NpcGreeting.KeyFor(Merchant("seed_keeper"), moment, 0, _ => true).Should().Be("Npc_seed_keeper_Quest_buy_seeds");
        NpcGreeting.KeyFor(Merchant("seed_keeper"), moment, 1, _ => true).Should().NotBe("Npc_seed_keeper_Quest_buy_seeds", "the Talk lines go on as before");
    }

    [Fact]
    public void KeyFor_TheActiveQuestIsElsewhere_TheGreetingIsWhatItWasWithoutAQuest()
    {
        NpcLineContext withQuest = NpcLineContext.Of(3, Season.Spring, WeatherKind.Sun, 9, false, false, false, "buy_seeds");
        NpcLineContext without = Moment(3, Season.Spring, WeatherKind.Sun, 9);

        NpcGreeting.KeyFor(Merchant("tailor"), withQuest, 0, _ => true).Should().Be(NpcGreeting.KeyFor(Merchant("tailor"), without, 0, _ => true));
    }

    [Fact]
    public void KeyFor_TheQuestLineHasNoText_FallsBackToThePool()
    {
        NpcLineContext moment = NpcLineContext.Of(3, Season.Spring, WeatherKind.Sun, 9, false, false, false, "buy_seeds");

        NpcGreeting.KeyFor(Merchant("seed_keeper"), moment, 0, key => key != "Npc_seed_keeper_Quest_buy_seeds").Should().StartWith("Npc_seed_keeper_Line_");
    }

    [Fact]
    public void KeyFor_SkipsALineWhoseTextIsMissing()
    {
        NpcLineContext moment = Moment(2, Season.Winter, WeatherKind.Snow, 8);
        NpcLine first = NpcLineCatalog.PoolOf(s_seed.Id).First(line => NpcGreeting.Fits(line.Tags, moment.Tags));
        string missing = KeyOf(s_seed, first);

        string key = NpcGreeting.KeyFor(s_seed, moment, 0, candidate => candidate != missing);

        key.Should().NotBe(missing);
    }

    [Theory]
    [InlineData(NpcLineTags.Spring, NpcLineTags.Spring | NpcLineTags.Morning, true)]
    [InlineData(NpcLineTags.Spring | NpcLineTags.Morning, NpcLineTags.Spring | NpcLineTags.Day, false)]
    [InlineData(NpcLineTags.Spring, NpcLineTags.Summer | NpcLineTags.Day, false)]
    [InlineData(NpcLineTags.None, NpcLineTags.Winter | NpcLineTags.Snow, true)]
    [InlineData(NpcLineTags.Wet, NpcLineTags.Sun | NpcLineTags.Day, false)]
    [InlineData(NpcLineTags.DeepMiner, NpcLineTags.Newcomer, false)]
    public void Fits_NeedsOneValueInEachGroupTheLineIsTaggedIn(NpcLineTags line, NpcLineTags now, bool expected)
    {
        NpcGreeting.Fits(line, now).Should().Be(expected);
    }

    [Theory]
    [InlineData(4, NpcLineTags.Evening)]
    [InlineData(5, NpcLineTags.Morning)]
    [InlineData(10, NpcLineTags.Morning)]
    [InlineData(11, NpcLineTags.Day)]
    [InlineData(16, NpcLineTags.Day)]
    [InlineData(17, NpcLineTags.Evening)]
    [InlineData(23, NpcLineTags.Evening)]
    public void TimeTag_SplitsTheDayIntoMorningDayAndEvening(int hour, NpcLineTags expected)
    {
        NpcLineContext.TimeTag(hour).Should().Be(expected);
    }

    [Theory]
    [InlineData(WeatherKind.Sun, NpcLineTags.Sun)]
    [InlineData(WeatherKind.Rain, NpcLineTags.Wet)]
    [InlineData(WeatherKind.Storm, NpcLineTags.Wet)]
    [InlineData(WeatherKind.Snow, NpcLineTags.Snow)]
    public void WeatherTag_RainAndStormShareOneLine(WeatherKind weather, NpcLineTags expected)
    {
        NpcLineContext.WeatherTag(weather).Should().Be(expected);
    }

    [Fact]
    public void Of_AProgressFlagIsOnlyThereWhenTrue()
    {
        NpcLineContext.Of(1, null, null, null, newcomer: false, harvested: true, deepMiner: false).Tags.Should().Be(NpcLineTags.Harvested);
        NpcLineContext.Of(1, null, null, null, newcomer: true, harvested: false, deepMiner: true).Tags.Should().Be(NpcLineTags.Newcomer | NpcLineTags.DeepMiner);
    }

    public static IEnumerable<object[]> Merchants() => s_merchants.Select(id => new object[] { id });
}

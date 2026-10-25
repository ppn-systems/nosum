// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Weather;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The day/night curve: dawn, day, dusk from 18:00, night from 20:00 (docs/adr/ADR-027 D4).</summary>
public sealed class AmbientLightRulesTests
{
    private static int Minute(int hour, int minute = 0) => (hour * 60) + minute;

    [Theory]
    [InlineData(7)]
    [InlineData(12)]
    [InlineData(17)]
    [InlineData(18)]
    public void At_InDaylight_HasNoTint(int hour)
        => AmbientLightRules.At(Minute(hour), WeatherKind.Sun, indoors: false).IsVisible.Should().BeFalse();

    [Fact]
    public void At_FromTwentyOClockToTheBoundary_IsFullNight()
    {
        AmbientTint night = AmbientLightRules.At(Minute(20), WeatherKind.Sun, indoors: false);

        night.A.Should().BeGreaterThan(150);
        AmbientLightRules.At(Minute(23, 59), WeatherKind.Sun, indoors: false).Should().Be(night);
        AmbientLightRules.At(Minute(2), WeatherKind.Sun, indoors: false).Should().Be(night);
    }

    [Fact]
    public void At_DuskBetweenSixAndEight_GetsDarkerMonotonically()
    {
        int previous = -1;
        for (int minute = Minute(18); minute <= Minute(20); minute += 5)
        {
            int opacity = AmbientLightRules.At(minute, WeatherKind.Sun, indoors: false).A;
            opacity.Should().BeGreaterThanOrEqualTo(previous);
            previous = opacity;
        }

        AmbientLightRules.At(Minute(19), WeatherKind.Sun, indoors: false).A.Should().BeInRange(1, 167);
    }

    [Fact]
    public void At_DawnBetweenFiveAndSeven_GetsLighterMonotonically()
    {
        int previous = int.MaxValue;
        for (int minute = Minute(5); minute <= Minute(7); minute += 5)
        {
            int opacity = AmbientLightRules.At(minute, WeatherKind.Sun, indoors: false).A;
            opacity.Should().BeLessThanOrEqualTo(previous);
            previous = opacity;
        }

        AmbientLightRules.At(Minute(7), WeatherKind.Sun, indoors: false).IsVisible.Should().BeFalse();
    }

    [Fact]
    public void At_IsContinuousAcrossTheWholeDay()
    {
        AmbientTint last = AmbientLightRules.At(0, WeatherKind.Sun, indoors: false);
        for (int minute = 1; minute < 24 * 60; minute++)
        {
            AmbientTint now = AmbientLightRules.At(minute, WeatherKind.Sun, indoors: false);
            System.Math.Abs(now.A - last.A).Should().BeLessThanOrEqualTo(3, $"minute {minute}");
            last = now;
        }
    }

    [Fact]
    public void At_OutOfRangeMinutes_Wrap()
    {
        AmbientLightRules.At(Minute(12) + (24 * 60), WeatherKind.Sun, indoors: false).Should().Be(AmbientLightRules.At(Minute(12), WeatherKind.Sun, indoors: false));
        AmbientLightRules.At(-60, WeatherKind.Sun, indoors: false).Should().Be(AmbientLightRules.At(Minute(23), WeatherKind.Sun, indoors: false));
    }

    [Theory]
    [InlineData(WeatherKind.Rain)]
    [InlineData(WeatherKind.Storm)]
    public void At_InRainAtNoon_DarkensTheDaySlightly(WeatherKind weather)
    {
        AmbientTint rainy = AmbientLightRules.At(Minute(12), weather, indoors: false);

        rainy.IsVisible.Should().BeTrue();
        rainy.A.Should().BeLessThan(80, "rain only dims the day a little");
    }

    [Fact]
    public void At_InRainAtNight_IsDarkerThanClearNight()
        => AmbientLightRules.At(Minute(22), WeatherKind.Rain, indoors: false).A.Should().BeGreaterThan(AmbientLightRules.At(Minute(22), WeatherKind.Sun, indoors: false).A);

    [Fact]
    public void At_IndoorsByDay_HasNoTintEvenInRain()
        => AmbientLightRules.At(Minute(12), WeatherKind.Storm, indoors: true).IsVisible.Should().BeFalse();

    [Fact]
    public void At_IndoorsAtNight_IsMildlyTinted()
    {
        AmbientTint room = AmbientLightRules.At(Minute(22), WeatherKind.Sun, indoors: true);

        room.IsVisible.Should().BeTrue();
        room.A.Should().BeLessThan(AmbientLightRules.At(Minute(22), WeatherKind.Sun, indoors: false).A);
        room.A.Should().BeLessThanOrEqualTo(AmbientLightRules.IndoorNightOpacity);
    }
}

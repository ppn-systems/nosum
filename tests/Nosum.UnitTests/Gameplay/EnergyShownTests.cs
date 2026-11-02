using FluentAssertions;
using Nosum.Gameplay.Energy;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>Pins the energy a client shows after the game day changes (docs/adr/ADR-114 D1).</summary>
public sealed class EnergyShownTests
{
    [Fact]
    public void Shown_SameDayAsTheReading_ShowsTheReadEnergy()
    {
        EnergyRules.Shown(energy: 0, maxEnergy: 270, readDay: 5, today: 5).Should().Be(0, "the server has not refilled yet on this day");
    }

    [Fact]
    public void Shown_LaterDayThanTheReading_ShowsFullEnergy()
    {
        EnergyRules.Shown(energy: 0, maxEnergy: 270, readDay: 5, today: 6).Should().Be(270, "the server refills on its next read after the day changes");
    }

    [Fact]
    public void Shown_NoReadDay_ShowsTheReadEnergy()
    {
        EnergyRules.Shown(energy: 12, maxEnergy: 270, readDay: null, today: 9).Should().Be(12, "a reading taken without a known calendar is not rolled over");
    }

    [Fact]
    public void Shown_ReadingFromAFewDaysBack_ShowsFullEnergy()
    {
        EnergyRules.Shown(energy: 40, maxEnergy: 270, readDay: 2, today: 9).Should().Be(270);
    }
}

using FluentAssertions;
using Nosum.Gameplay.Skills;
using Nosum.Wasm.ViewModels.Progress;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>Pins which level-ups show an energy perk line (docs/adr/ADR-089 D3).</summary>
public sealed class LevelUpPerkTests
{
    [Theory]
    [InlineData(SkillKind.Mining, 4, 1)]
    [InlineData(SkillKind.Foraging, 8, 2)]
    [InlineData(SkillKind.Farming, 4, 1)]
    public void PerkAt_GatheringSkillAtAFourthLevel_GivesTheDiscountNow(SkillKind skill, int level, int expected)
    {
        LevelUpBannerViewModel.PerkAt(skill, level).Should().Be(expected);
    }

    [Theory]
    [InlineData(SkillKind.Mining, 1)]
    [InlineData(SkillKind.Mining, 5)]
    [InlineData(SkillKind.Mining, 10)]
    public void PerkAt_LevelThatUnlocksNothing_IsNull(SkillKind skill, int level)
    {
        LevelUpBannerViewModel.PerkAt(skill, level).Should().BeNull("only the fourth and eighth levels change the discount");
    }

    [Theory]
    [InlineData(SkillKind.Combat)]
    [InlineData(SkillKind.Fishing)]
    [InlineData(SkillKind.None)]
    public void PerkAt_NonGatheringSkill_IsNullAtAnyLevel(SkillKind skill)
    {
        LevelUpBannerViewModel.PerkAt(skill, 4).Should().BeNull("their tools cost no gathering energy");
    }
}

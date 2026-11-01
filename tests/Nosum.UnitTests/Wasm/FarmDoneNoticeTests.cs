// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Nosum.Contracts.Farm;
using Nosum.Gameplay.Items;
using Nosum.Wasm.ViewModels.Feedback;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class FarmDoneNoticeTests
{
    [Theory]
    [InlineData(FarmAction.Tilled, "Farm_Done_Tilled")]
    [InlineData(FarmAction.Planted, "Farm_Done_Planted")]
    [InlineData(FarmAction.Watered, "Farm_Done_Watered")]
    [InlineData(FarmAction.Harvested, "Farm_Done_Harvested")]
    public void KeyOf_WorkedUse_NamesItsAction(FarmAction action, string key)
    {
        FarmDoneNotice.KeyOf(FarmStatus.Ok, action, 1, ItemCatalog.Scythe).Should().Be(key);
    }

    [Fact]
    public void KeyOf_HarvestWithAnotherItemInHand_KeepsTheHarvestedWithOtherItemLine()
    {
        FarmDoneNotice.KeyOf(FarmStatus.Ok, FarmAction.Harvested, 1, ItemCatalog.WateringCan).Should().Be("Farm_HarvestedWithOtherItem");
    }

    [Theory]
    [InlineData(FarmStatus.NotMature, FarmAction.None)]
    [InlineData(FarmStatus.AlreadyWet, FarmAction.None)]
    [InlineData(FarmStatus.NothingToDo, FarmAction.None)]
    public void KeyOf_Refusal_IsSilentHere(FarmStatus status, FarmAction action)
    {
        // A refusal has its own line (FarmStatus text); this line is only for a use that worked.
        FarmDoneNotice.KeyOf(status, action, 1, ItemCatalog.WateringCan).Should().BeNull();
    }

    [Theory]
    [InlineData(FarmAction.Descended)]
    [InlineData(FarmAction.Hit)]
    [InlineData(FarmAction.Broke)]
    [InlineData(FarmAction.None)]
    public void KeyOf_WorkedUseWithNoLine_IsSilent(FarmAction action)
    {
        FarmDoneNotice.KeyOf(FarmStatus.Ok, action, 1, ItemCatalog.WateringCan).Should().BeNull();
    }

    [Fact]
    public void Keys_AreInBothLanguages()
    {
        // Each key the notice can return exists in the Vietnamese default and the English file (docs/adr/ADR-010).
        string[] keys = ["Farm_Done_Tilled", "Farm_Done_Planted", "Farm_Done_Watered", "Farm_Done_Harvested", "Farm_HarvestedWithOtherItem"];
        HashSet<string> vi = SocialClientFixtures.Keys("Language.resx");
        HashSet<string> en = SocialClientFixtures.Keys("Language.en.resx");
        foreach (string key in keys)
        {
            vi.Should().Contain(key);
            en.Should().Contain(key);
        }
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Gameplay.Npcs;
using Nosum.Wasm.ViewModels.Minimap;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class MinimapMarkerRulesTests
{
    [Fact]
    public void NpcColour_Merchant_IsTheShopColour()
        => MinimapMarkerRules.NpcColour(NpcActions.Buy | NpcActions.Talk).Should().Be(MinimapMarkerRules.ShopColour);

    [Fact]
    public void NpcColour_TalkOnly_IsTheTalkColour()
        => MinimapMarkerRules.NpcColour(NpcActions.Talk).Should().Be(MinimapMarkerRules.TalkColour);

    [Fact]
    public void NpcColour_ShopColourIsNotTheTalkColour()
        => MinimapMarkerRules.ShopColour.Should().NotBe(MinimapMarkerRules.TalkColour, "a merchant must stand out from the other NPCs");
}

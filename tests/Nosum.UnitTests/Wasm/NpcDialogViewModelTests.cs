// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Npcs;
using Nosum.Gameplay.Shops;
using Nosum.Wasm.ViewModels;
using Nosum.Wasm.ViewModels.Npc;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>
/// The merchants' dialog (docs/adr/ADR-043 D3): the action key at a counter opens the dialog box, its buttons open the shop, market, carpenter, blacksmith or board as a
/// dialog of their own, and nothing is ever a tab of the game menu.
/// </summary>
[Collection(SharedUiCulture.Name)]
public sealed class NpcDialogViewModelTests
{
    private static NpcDialogViewModel Create() => new(WasmTestSupport.Localizer<Language>());

    [Fact]
    public void Interact_AtACounter_OpensTheDialogBoxOfItsMerchantWithTheGreeting()
    {
        using CultureScope scope = new("en");
        NpcDialogViewModel vm = Create();
        int changes = 0;
        vm.Changed += () => changes++;

        vm.Interact(ShopCatalog.SeedShop).Should().BeTrue();

        vm.Stage.Should().Be(NpcStage.Speaking);
        vm.Npc!.Id.Should().Be("seed_keeper");
        vm.Name.Should().Be("Mai");
        vm.Role.Should().Be("Seed shopkeeper");
        vm.Line.Should().Be(vm.Text("Npc_seed_keeper_Greeting"));
        vm.PortraitUrl.Should().Be("assets/sprites/npcs/seed_keeper_portrait.png");
        vm.Page.Should().Be(NpcPage.None);
        vm.Panel.Should().Be(PanelKind.None, "the box is not a page of the menu");
        changes.Should().Be(1);
    }

    [Fact]
    public void Interact_AtACounterWithNoMerchant_OrWhileTalking_DoesNothing()
    {
        NpcDialogViewModel vm = Create();

        vm.Interact(999).Should().BeFalse();
        vm.Stage.Should().Be(NpcStage.Closed);

        vm.Interact(ShopCatalog.SeedShop).Should().BeTrue();
        vm.Interact(ShopCatalog.Blacksmith).Should().BeFalse("one conversation at a time");
        vm.Npc!.Id.Should().Be("seed_keeper");
    }

    [Fact]
    public void Choose_BuyAtAShop_OpensThatShopAsADialogOfItsOwn()
    {
        NpcDialogViewModel vm = Create();
        vm.Interact(ShopCatalog.SeedShop);

        vm.Choose(NpcActions.Buy);

        vm.Stage.Should().Be(NpcStage.Trading);
        vm.Page.Should().Be(NpcPage.Shop);
        vm.Panel.Should().Be(PanelKind.Shop);
        vm.ShopId.Should().Be(ShopCatalog.SeedShop);
    }

    [Theory]
    [InlineData(ShopCatalog.ClothingShop, NpcActions.Buy, NpcPage.Shop, PanelKind.Shop)]
    [InlineData(ShopCatalog.FurnitureShop, NpcActions.Buy, NpcPage.Shop, PanelKind.Shop)]
    [InlineData(ShopCatalog.Blacksmith, NpcActions.Buy, NpcPage.Shop, PanelKind.Shop)]
    [InlineData(ShopCatalog.Blacksmith, NpcActions.Upgrade, NpcPage.Upgrade, PanelKind.Shop)]
    [InlineData(ShopCatalog.CarpenterShop, NpcActions.Buy, NpcPage.Shop, PanelKind.Shop)]
    [InlineData(ShopCatalog.CarpenterShop, NpcActions.Build, NpcPage.Build, PanelKind.Shop)]
    [InlineData(MarketRules.MarketCounterId, NpcActions.Buy, NpcPage.MarketBuy, PanelKind.Market)]
    [InlineData(MarketRules.MarketCounterId, NpcActions.Sell, NpcPage.MarketSell, PanelKind.Market)]
    [InlineData(CommunityGoalCatalog.BoardCounterId, NpcActions.Goals, NpcPage.Goals, PanelKind.Goals)]
    public void Choose_EachButton_OpensItsPage(int counterId, NpcActions action, NpcPage page, PanelKind panel)
    {
        NpcDialogViewModel vm = Create();
        vm.Interact(counterId);

        vm.Choose(action);

        vm.Stage.Should().Be(NpcStage.Trading);
        vm.Page.Should().Be(page);
        vm.Panel.Should().Be(panel);
        vm.PageTitle.Should().NotBeNullOrWhiteSpace().And.NotStartWith("Shop_").And.NotStartWith("Toolbar_");
    }

    [Fact]
    public void Choose_AnActionTheMerchantDoesNotOffer_IsIgnored()
    {
        NpcDialogViewModel vm = Create();
        vm.Interact(ShopCatalog.SeedShop);

        vm.Choose(NpcActions.Sell);
        vm.Choose(NpcActions.Build);
        vm.Choose(NpcActions.Upgrade);

        vm.Stage.Should().Be(NpcStage.Speaking);
        vm.Page.Should().Be(NpcPage.None);
    }

    [Theory]
    [InlineData(ShopCatalog.SeedShop, "Buy,Talk")]
    [InlineData(ShopCatalog.Blacksmith, "Buy,Upgrade,Talk")]
    [InlineData(ShopCatalog.CarpenterShop, "Buy,Build,Talk")]
    [InlineData(MarketRules.MarketCounterId, "Buy,Sell,Talk")]
    [InlineData(CommunityGoalCatalog.BoardCounterId, "Goals,Talk")]
    public void Choices_AreOnlyWhatTheCounterSupports_InAFixedOrder(int counterId, string expected)
    {
        using CultureScope scope = new("en");
        NpcDialogViewModel vm = Create();
        vm.Interact(counterId);

        vm.Choices.Select(c => c.Action.ToString()).Should().Equal(expected.Split(','));
        vm.Choices.Should().OnlyContain(c => c.Label.Length > 0 && !c.Label.StartsWith("Npc_", StringComparison.Ordinal));
        vm.LeaveLabel.Should().Be("Leave");
    }

    [Fact]
    public void Choose_Talk_CyclesThroughTheMerchantsLinesAndStaysInTheBox()
    {
        using CultureScope scope = new("en");
        NpcDialogViewModel vm = Create();
        vm.Interact(ShopCatalog.Blacksmith);
        string greeting = vm.Line;

        vm.Choose(NpcActions.Talk);
        string first = vm.Line;
        vm.Choose(NpcActions.Talk);
        string second = vm.Line;
        vm.Choose(NpcActions.Talk);

        vm.Stage.Should().Be(NpcStage.Speaking);
        first.Should().Be(vm.Text("Npc_blacksmith_Talk1")).And.NotBe(greeting);
        second.Should().Be(vm.Text("Npc_blacksmith_Talk2")).And.NotBe(first);
        vm.Line.Should().Be(first, "the lines go round");
    }

    [Fact]
    public void Back_FromADialog_ReturnsToTheBoxAndLeaveEndsTheConversation()
    {
        NpcDialogViewModel vm = Create();
        vm.Interact(MarketRules.MarketCounterId);
        vm.Choose(NpcActions.Sell);

        vm.Back();

        vm.Stage.Should().Be(NpcStage.Speaking);
        vm.Page.Should().Be(NpcPage.None);
        vm.Panel.Should().Be(PanelKind.None);

        vm.Leave();

        vm.Stage.Should().Be(NpcStage.Closed);
        vm.Npc.Should().BeNull();
        vm.Interact(MarketRules.MarketCounterId).Should().BeTrue("a new conversation can begin");
    }

    [Fact]
    public void CounterLeft_EndsOnlyTheConversationAtThatCounter()
    {
        NpcDialogViewModel vm = Create();
        vm.Interact(ShopCatalog.CarpenterShop);
        vm.Choose(NpcActions.Build);

        vm.CounterLeft(ShopCatalog.SeedShop);
        vm.Stage.Should().Be(NpcStage.Trading, "another counter was left");

        vm.CounterLeft(ShopCatalog.CarpenterShop);
        vm.Stage.Should().Be(NpcStage.Closed);
        vm.Panel.Should().Be(PanelKind.None);
    }

    [Theory]
    [InlineData("vi")]
    [InlineData("en")]
    public void EveryMerchant_SpeaksInBothLanguages_WithoutShowingAKey(string culture)
    {
        using CultureScope scope = new(culture);

        foreach (NpcDefinition npc in NpcCatalog.All)
        {
            NpcDialogViewModel vm = Create();
            vm.Interact(npc.CounterId);

            vm.Name.Should().NotStartWith("Npc_");
            vm.Role.Should().NotStartWith("Npc_");
            vm.Line.Should().NotStartWith("Npc_").And.NotBeNullOrWhiteSpace();
            vm.Choices.Should().OnlyContain(c => !c.Label.StartsWith("Npc_", StringComparison.Ordinal));
            vm.LeaveLabel.Should().NotStartWith("Npc_");
            foreach (NpcChoice choice in vm.Choices.Where(c => c.Action != NpcActions.Talk))
            {
                vm.Choose(choice.Action);
                vm.PageTitle.Should().NotBeNullOrWhiteSpace();
                vm.Back();
            }
        }
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _ui = CultureInfo.CurrentUICulture;
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

        public CultureScope(string culture)
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            CultureInfo.CurrentCulture = new CultureInfo(culture);
        }

        public void Dispose()
        {
            CultureInfo.CurrentUICulture = _ui;
            CultureInfo.CurrentCulture = _culture;
        }
    }
}

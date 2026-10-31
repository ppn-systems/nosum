// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Mine.S2C;
using Nosum.Contracts.Quest;
using Nosum.Contracts.Quest.S2C;
using Nosum.Contracts.Season.S2C;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Npcs;
using Nosum.Gameplay.Quests;
using Nosum.Gameplay.Shops;
using Nosum.Gameplay.Weather;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Mine;
using Nosum.Wasm.Socket.Quest;
using Nosum.Wasm.ViewModels;
using Nosum.Wasm.ViewModels.Npc;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.ViewModels.Settings;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>
/// The merchants' dialog (docs/adr/ADR-043 D3): the action key at a counter opens the dialog box, its buttons open the shop, market, carpenter, blacksmith or board as a
/// dialog of their own, and nothing is ever a tab of the game menu.
/// </summary>
[Collection(SharedUiCulture.Name)]
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the view model and its reveal timer die with the test.")]
public sealed class NpcDialogViewModelTests
{
    // Reduced motion by default, so a line is whole at once; the typewriter tests turn it off.
    private static NpcDialogViewModel Create(bool reduceMotion = true) => CreateWith(new CalendarChannel(new FakeGameSocket()), reduceMotion);

    private static NpcDialogViewModel CreateWith(CalendarChannel calendar, bool reduceMotion = true, QuestChannel? quests = null, MineChannel? mine = null)
        => new(
            WasmTestSupport.Localizer<Language>(),
            calendar,
            quests ?? new QuestChannel(new FakeGameSocket()),
            mine ?? new MineChannel(new FakeGameSocket()),
            Settings(reduceMotion));

    // The line of a merchant's conversation at a slot, for a farmer who has finished no quest and whose calendar is not known yet.
    private static string LineOf(NpcDialogViewModel vm, string npcId, int slot)
    {
        NpcDefinition npc = NpcCatalog.TryGet(npcId, out NpcDefinition? found) ? found! : throw new InvalidOperationException(npcId);
        return vm.Text(NpcGreeting.KeyFor(npc, NpcLineContext.Of(0, null, null, null, newcomer: true, harvested: false, deepMiner: false), slot, _ => true));
    }

    private static SettingsViewModel Settings(bool reduceMotion) => new(WasmTestSupport.Text(), new FixedStore(reduceMotion ? "calm=1" : "calm=0"));

    private sealed class FixedStore(string text) : ISettingsStore
    {
        public string? Read() => text;

        public void Write(string value)
        {
            _ = value;
        }
    }

    [Fact]
    public void Interact_WithMotion_TypesTheLineOutAndHoldsTheChoices()
    {
        using CultureScope scope = new("en");
        NpcDialogViewModel vm = Create(reduceMotion: false);

        vm.Interact(ShopCatalog.SeedShop);

        vm.IsRevealing.Should().BeTrue();
        vm.PortraitTalking.Should().BeTrue("the portrait speaks while the line types out");
        vm.Choices.Should().BeEmpty("the choices come once the line is done");
    }

    [Fact]
    public void Skip_WhileTyping_ShowsTheWholeLineAndTheChoices()
    {
        using CultureScope scope = new("en");
        NpcDialogViewModel vm = Create(reduceMotion: false);
        vm.Interact(ShopCatalog.SeedShop);

        vm.Skip();

        vm.IsRevealing.Should().BeFalse();
        vm.PortraitTalking.Should().BeFalse();
        vm.Line.Should().Be(LineOf(vm, "seed_keeper", 0));
        vm.Choices.Should().NotBeEmpty();
    }

    [Fact]
    public void Interact_WithReducedMotion_ShowsTheWholeLineAtOnce()
    {
        using CultureScope scope = new("en");
        NpcDialogViewModel vm = Create(reduceMotion: true);

        vm.Interact(ShopCatalog.SeedShop);

        vm.IsRevealing.Should().BeFalse();
        vm.Line.Should().Be(LineOf(vm, "seed_keeper", 0));
        vm.Choices.Should().NotBeEmpty();
    }

    [Fact]
    public void Skip_WithNothingTyping_DoesNothing()
    {
        using CultureScope scope = new("en");
        NpcDialogViewModel vm = Create(reduceMotion: true);
        vm.Interact(ShopCatalog.SeedShop);
        int changes = 0;
        vm.Changed += () => changes++;

        vm.Skip();

        changes.Should().Be(0);
    }

    [Fact]
    public void Line_OnARainySpringDayWithTheCalendarKnown_SaysALineForTheWeatherAndHour()
    {
        using CultureScope scope = new("en");
        GameCalendar calendar = GameCalendar.Default;
        int rainyDay = 0;
        for (int day = 1; rainyDay == 0 && day <= GameCalendar.DaysPerSeason; day++)
        {
            if (WeatherRules.ForDay(calendar, day) == WeatherKind.Rain)
            {
                rainyDay = day;
            }
        }

        rainyDay.Should().BeGreaterThan(0, "the default calendar has a rainy spring day");
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new CalendarInfo
        {
            EpochMs = calendar.EpochMs,
            DayLengthMs = calendar.DayLengthMs,
            ServerNowMs = calendar.DayStartMs(rainyDay) + (calendar.DayLengthMs / 2),
        });
        CalendarChannel channel = new(socket);
        channel.Load();
        NpcDialogViewModel vm = CreateWith(channel);
        long now = calendar.DayStartMs(rainyDay) + (calendar.DayLengthMs / 2);
        NpcDefinition seed = NpcCatalog.TryGet("seed_keeper", out NpcDefinition? found) ? found! : throw new InvalidOperationException();
        string expected = vm.Text(NpcGreeting.KeyFor(
            seed,
            NpcLineContext.Of(calendar.DayNumberAt(now), Season.Spring, WeatherKind.Rain, calendar.At(now).Hour, newcomer: true, harvested: false, deepMiner: false),
            0,
            _ => true));

        vm.Interact(ShopCatalog.SeedShop).Should().BeTrue();

        vm.Line.Should().Be(expected);
    }

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
        vm.Line.Should().Be(LineOf(vm, "seed_keeper", 0));
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
    [InlineData(CommunityGoalCatalog.BoardCounterId, NpcActions.Coop, NpcPage.Coop, PanelKind.Coop)]
    [InlineData(ShopCatalog.ClothingShop, NpcActions.Wardrobe, NpcPage.Wardrobe, PanelKind.Wardrobe)]
    [InlineData(ShopCatalog.ClothingShop, NpcActions.Backpack, NpcPage.Backpack, PanelKind.Backpack)]
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
    [InlineData(CommunityGoalCatalog.BoardCounterId, "Goals,Coop,Talk")]
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
        greeting.Should().Be(LineOf(vm, "blacksmith", 0));
        first.Should().Be(LineOf(vm, "blacksmith", 1)).And.NotBe(greeting);
        second.Should().Be(LineOf(vm, "blacksmith", 2)).And.NotBe(first);
        vm.Line.Should().Be(LineOf(vm, "blacksmith", 3), "the lines go round");
    }

    [Fact]
    public void Line_AfterTheFirstHarvestAndTheTenthFloor_SaysTheProgressLines()
    {
        using CultureScope scope = new("en");
        FakeGameSocket questSocket = new();
        questSocket.ReplyWith(_ => FinishedFirstHarvest());
        QuestChannel quests = new(questSocket);
        quests.Load();
        FakeGameSocket mineSocket = new();
        mineSocket.ReplyWith(_ => new MineState { Deepest = NpcLineContext.DeepMinerFloor });
        MineChannel mine = new(mineSocket);
        mine.Load();
        NpcDialogViewModel vm = CreateWith(new CalendarChannel(new FakeGameSocket()), quests: quests, mine: mine);
        NpcDefinition seed = NpcCatalog.TryGet("seed_keeper", out NpcDefinition? found) ? found! : throw new InvalidOperationException();
        string expected = vm.Text(NpcGreeting.KeyFor(seed, NpcLineContext.Of(0, null, null, null, newcomer: false, harvested: true, deepMiner: true), 0, _ => true));

        vm.Interact(ShopCatalog.SeedShop).Should().BeTrue();

        vm.Line.Should().Be(expected, "a farmer who harvested and reached the tenth floor is not a newcomer");
    }

    private static QuestLog FinishedFirstHarvest()
    {
        QuestLog log = new();
        log.Quests.Add(new QuestRow { QuestId = QuestCatalog.FirstHarvest, State = QuestState.Completed, Progress = [1, 1] });
        return log;
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

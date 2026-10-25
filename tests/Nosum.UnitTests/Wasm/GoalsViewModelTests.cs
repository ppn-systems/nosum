// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Goals;
using Nosum.Contracts.Goals.C2S;
using Nosum.Contracts.Goals.S2C;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Season.S2C;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Goals;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.ViewModels;
using Nosum.Wasm.ViewModels.Goals;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The Town board page: the channel mirrors what the server says and pushes, the view model shows it localized and sends deliveries (docs/adr/ADR-030).</summary>
public sealed class GoalsViewModelTests
{
    private const int Winter = CommunityGoalCatalog.WinterLanterns;

    [Fact]
    public void Goals_AfterLoad_ShowNameProgressPercentAndTheViewersPart_InEnglish()
    {
        using CultureScope scope = new("en", "en-US");
        Fixture f = Fixture.Create(parsnipsHeld: 0);

        f.Vm.Open();

        GoalDisplay goal = f.Vm.Goals.Should().ContainSingle().Subject;
        goal.GoalId.Should().Be(Winter);
        goal.Name.Should().Be("Lanterns for the town square");
        goal.ProgressText.Should().Be("250 / 1,000");
        goal.Percent.Should().Be(25);
        goal.MineText.Should().Be("You delivered 40");
        goal.ContributorsText.Should().Be("7 players took part");
        goal.State.Should().Be(GoalState.Active);
        goal.StateText.Should().Be("Open");
        goal.Item.ItemId.Should().Be(ItemCatalog.Parsnip);
        f.Vm.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public void Goals_InVietnamese_ShowTheVietnameseTexts()
    {
        using CultureScope scope = new("vi", "vi-VN");
        Fixture f = Fixture.Create(parsnipsHeld: 0);

        f.Vm.Open();

        GoalDisplay goal = f.Vm.Goals.Single();
        goal.Name.Should().Be("Đèn lồng quảng trường");
        goal.MineText.Should().Be("Bạn đã nộp 40");
        goal.StateText.Should().Be("Đang mở");
    }

    [Fact]
    public void Goals_BeforeTheServerSpoke_AreEmptyAndNotLoaded()
    {
        Fixture f = Fixture.Create(parsnipsHeld: 0);

        f.Vm.Goals.Should().BeEmpty();
        f.Vm.IsLoaded.Should().BeFalse();
        f.Vm.SeasonEndText.Should().BeEmpty();
    }

    [Fact]
    public void Push_ChangesTheTotalAndTheBar_AndTellsTheView_WithoutTouchingThePlayersOwnPart()
    {
        Fixture f = Fixture.Create(parsnipsHeld: 0);
        f.Vm.Open();
        int raised = 0;
        f.Vm.Changed += () => raised++;

        f.PushProgress(Winter, 3, 600, GoalState.Active, 9);

        raised.Should().BeGreaterThan(0);
        GoalDisplay goal = f.Vm.Goals.Single();
        goal.Percent.Should().Be(60);
        goal.ContributorsText.Should().Contain("9");
        f.Vm.Goals.Single().MineText.Should().Contain("40");
    }

    [Fact]
    public void Push_CompletedGoal_ShowsTheStateAndNothingToDeliver()
    {
        Fixture f = Fixture.Create(parsnipsHeld: 50);
        f.Vm.Open();

        f.PushProgress(Winter, 3, 1_000, GoalState.Completed, 12);

        GoalDisplay goal = f.Vm.Goals.Single();
        (goal.Percent, goal.State, goal.CanDeliver).Should().Be((100, GoalState.Completed, false));
    }

    [Fact]
    public void Push_ForAGoalTheMirrorDoesNotKnow_ReadsTheGoalsAgain()
    {
        Fixture f = Fixture.Create(parsnipsHeld: 0);
        f.Vm.Open();
        int reads = f.Socket.Requests.OfType<GetGoals>().Count();

        f.PushProgress(CommunityGoalCatalog.SpringBridge, 4, 10, GoalState.Active, 1);

        f.Socket.Requests.OfType<GetGoals>().Count().Should().Be(reads + 1);
    }

    [Fact]
    public void Deliver_SendsTheGoalAndTheTypedAmount_AndShowsTheOutcome()
    {
        using CultureScope scope = new("en", "en-US");
        Fixture f = Fixture.Create(parsnipsHeld: 120);
        f.Vm.Open();
        f.Vm.SetAmount(Winter, "75");

        f.Vm.Deliver(Winter);

        f.Delivered.Should().Equal((Winter, 75));
        f.Vm.Status.Should().Be("Delivered 75. Thank you!");
        f.Vm.StatusIsGood.Should().BeTrue();
    }

    [Fact]
    public void Deliver_WithoutTyping_OffersEverythingThePlayerHolds_UpToWhatTheGoalStillTakes()
    {
        Fixture f = Fixture.Create(parsnipsHeld: 120);
        f.Vm.Open();
        f.Vm.Goals.Single().AmountText.Should().Be("120");
        f.Vm.Goals.Single().Held.Should().Be(120);

        // 900 are still missing at most: a player with 2 000 would be offered 750 (the remainder) - here only 750 can fit after the push.
        f.PushProgress(Winter, 3, 950, GoalState.Active, 9);
        f.Vm.Goals.Single().AmountText.Should().Be("50", "only 50 more fit");

        f.Vm.Deliver(Winter);

        f.Delivered.Should().Equal((Winter, 50));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("1.5")]
    public void Deliver_AnAmountThatIsNotAPositiveWholeNumber_IsRefusedHereAndNothingIsSent(string typed)
    {
        using CultureScope scope = new("en", "en-US");
        Fixture f = Fixture.Create(parsnipsHeld: 120);
        f.Vm.Open();
        f.Vm.SetAmount(Winter, typed);

        f.Vm.Deliver(Winter);

        f.Delivered.Should().BeEmpty();
        f.Vm.Status.Should().Be("That amount is not valid.");
        f.Vm.StatusIsGood.Should().BeFalse();
    }

    [Theory]
    [InlineData(GoalStatus.NotAtBoard, "You are too far from the board.")]
    [InlineData(GoalStatus.NotEnough, "You do not have enough of the item.")]
    [InlineData(GoalStatus.Closed, "This goal is closed.")]
    [InlineData(GoalStatus.NotThisSeason, "This goal is not part of the current season.")]
    public void Deliver_AServerRefusal_ShowsItsText(GoalStatus status, string expected)
    {
        using CultureScope scope = new("en", "en-US");
        Fixture f = Fixture.Create(parsnipsHeld: 20, deliverStatus: status);
        f.Vm.Open();

        f.Vm.Deliver(Winter);

        f.Vm.Status.Should().Be(expected);
        f.Vm.StatusIsGood.Should().BeFalse();
    }

    [Fact]
    public void CanDeliver_NeedsAnOpenGoalAndTheItem()
    {
        Fixture none = Fixture.Create(parsnipsHeld: 0);
        none.Vm.Open();
        none.Vm.Goals.Single().CanDeliver.Should().BeFalse("no parsnips in the inventory");

        Fixture some = Fixture.Create(parsnipsHeld: 3);
        some.Vm.Open();
        some.Vm.Goals.Single().CanDeliver.Should().BeTrue();
    }

    [Fact]
    public void SeasonEndText_CountsTheGameDaysLeft()
    {
        using CultureScope scope = new("en", "en-US");
        Fixture f = Fixture.Create(parsnipsHeld: 0, daysLeft: 9);
        f.Vm.Open();

        f.Vm.SeasonEndText.Should().Be("Game days left in the season: 9.");
    }

    [Fact]
    public void Text_EveryGoalOutcomeStateAndGoalHasATextInBothLanguages()
    {
        List<string> language = [.. Enum.GetValues<GoalStatus>().Where(s => s != GoalStatus.Ok).Select(s => "Goals_Status_" + s)];
        language.AddRange(Enum.GetValues<GoalState>().Select(s => "Goals_State_" + s));
        language.AddRange(["Toolbar_Goals", "Goals_Title", "Goals_Hint", "Goals_Loading", "Goals_None", "Goals_SeasonEnds", "Goals_Amount", "Goals_Deliver", "Goals_Held", "Goals_Progress", "Goals_Mine", "Goals_Contributors", "Goals_Done"]);
        List<string> catalogue = [.. CommunityGoalCatalog.All.SelectMany(g => new[] { "Goal_" + g.Key + "_Name", "Goal_" + g.Key + "_Reward" })];

        Keys("Language.resx").Should().Contain(language);
        Keys("Language.en.resx").Should().Contain(language);
        Keys("Catalogue.resx").Should().Contain(catalogue);
        Keys("Catalogue.en.resx").Should().Contain(catalogue);
        Keys("Language.resx").Should().BeEquivalentTo(Keys("Language.en.resx"));
        Keys("Catalogue.resx").Should().BeEquivalentTo(Keys("Catalogue.en.resx"));
    }

    [Fact]
    public void Toolbar_TheBoardIsNotAMenuButton_ButHasItsOwnTab()
    {
        ToolbarViewModel toolbar = new(Localizer<Language>());

        toolbar.Items.Should().NotContain(i => i.Kind == PanelKind.Goals);
        toolbar.GoalsItem.Kind.Should().Be(PanelKind.Goals);
        toolbar.GoalsItem.Icon.Should().Be("tab-goals");
        toolbar.GoalsItem.Label.Should().NotBeNullOrWhiteSpace().And.NotStartWith("Toolbar_");
        ((int)PanelKind.Goals).Should().Be(17, "11 to 16 are Skills, Crafting, Chest, Furnace, Friends and Mail");
    }

    private static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    private static HashSet<string> Keys(string file, [CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
        string path = Path.Combine(root, "wasm", "Nosum.Wasm.ViewModels", "Resources", file);
        return XDocument.Load(path).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
    }

    // A view model on a fake socket: the server says the winter goal is at 250 of 1 000 with 40 from this player, the inventory holds some parsnips.
    private sealed class Fixture
    {
        public required FakeGameSocket Socket { get; init; }

        public required GoalsViewModel Vm { get; init; }

        public required List<(int Goal, int Quantity)> Delivered { get; init; }

        public void PushProgress(int goalId, int season, long total, GoalState state, int contributors)
        {
            using GoalProgress packet = new() { GoalId = goalId, SeasonNumber = season, Total = total, State = state, Contributors = contributors };
            this.Socket.Push(packet);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The channels live as long as the test.")]
        public static Fixture Create(int parsnipsHeld, GoalStatus deliverStatus = GoalStatus.Ok, int daysLeft = 0)
        {
            FakeGameSocket socket = new();
            List<(int, int)> delivered = [];
            socket.ReplyWith(_ =>
            {
                GoalsSnapshot snapshot = new() { ServerNowMs = 1_000_000, SeasonEndsAtMs = 1_000_000 + (daysLeft * 43_200_000L) - 1 };
                snapshot.Rows.Add(new GoalRow { GoalId = Winter, SeasonNumber = 3, Total = 250, State = GoalState.Active, Mine = 40, Contributors = 7 });
                return snapshot;
            });
            socket.ReplyWith<DeliverResult>(request =>
            {
                DeliverToGoal deliver = (DeliverToGoal)request;
                delivered.Add((deliver.GoalId, deliver.Quantity));
                return new DeliverResult { Status = deliverStatus, Delivered = deliverStatus == GoalStatus.Ok ? deliver.Quantity : 0 };
            });
            socket.ReplyWith(_ =>
            {
                InventorySnapshot snapshot = new() { Capacity = 36, SelectedSlot = 0 };
                if (parsnipsHeld > 0)
                {
                    snapshot.Slots.Add(new InventorySlotRow { Slot = 5, ItemId = ItemCatalog.Parsnip, Quantity = parsnipsHeld });
                }

                return snapshot;
            });
            socket.ReplyWith(_ => new CalendarInfo { EpochMs = 0, DayLengthMs = 43_200_000, ServerNowMs = 1_000_000 });

            InventoryChannel inventoryChannel = new(socket);
            InventoryViewModel inventory = new(inventoryChannel, new FixedIcons(), Localizer<Catalogue>(), Localizer<Language>());
            inventoryChannel.Load();
            CalendarChannel calendar = new(socket);
            calendar.Load();
            ItemDescriber describer = new(new FixedIcons(), Localizer<Catalogue>(), Localizer<Language>());
            GoalsViewModel vm = new(new GoalChannel(socket), inventory, describer, calendar, Localizer<Catalogue>(), Localizer<Language>());
            return new Fixture { Socket = socket, Vm = vm, Delivered = delivered };
        }
    }

    private sealed class FixedIcons : IItemIconSource
    {
        public bool TryGet(int itemId, out ItemIcon icon)
        {
            icon = new ItemIcon("sheet.png", 0, 0, 16, 64, 64);
            return true;
        }
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _ui = CultureInfo.CurrentUICulture;
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

        public CultureScope(string ui, string culture)
        {
            CultureInfo.CurrentUICulture = new CultureInfo(ui);
            CultureInfo.CurrentCulture = new CultureInfo(culture);
        }

        public void Dispose()
        {
            CultureInfo.CurrentUICulture = _ui;
            CultureInfo.CurrentCulture = _culture;
        }
    }
}

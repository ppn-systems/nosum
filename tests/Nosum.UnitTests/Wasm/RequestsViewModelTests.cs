// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Requests;
using Nosum.Contracts.Requests.C2S;
using Nosum.Contracts.Requests.S2C;
using Nosum.Contracts.Season.S2C;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Requests;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Requests;
using Xunit;
using static Nosum.UnitTests.Wasm.WasmTestSupport;

namespace Nosum.UnitTests.Wasm;

/// <summary>The requests tab of the Town board (docs/adr/ADR-036): the channel mirrors what the server says, the view model shows it localized and sends deliveries as intents.</summary>
[Collection(SharedUiCulture.Name)]
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the fake socket owns nothing, pooled packets and channels die with the test.")]
public sealed class RequestsViewModelTests
{
    private const int Day = 42;

    [Fact]
    public void Requests_AfterLoad_ShowAskRewardAndWhatThePlayerCarries_InEnglish()
    {
        using CultureScope scope = new("en");
        Fixture f = Fixture.Create(parsnips: 7);

        f.Vm.Open();

        f.Vm.IsLoaded.Should().BeTrue();
        RequestDisplay first = f.Vm.Requests[0];
        first.AskText.Should().Be("Deliver 5 × Parsnip");
        first.RewardText.Should().Be("Reward: 130 coins");
        first.HeldText.Should().Be("You carry 7");
        first.Filled.Should().BeFalse();
        first.CanDeliver.Should().BeTrue();
        first.Item.ItemId.Should().Be(ItemCatalog.Parsnip);
        first.Item.Quantity.Should().Be(5);
        RequestDisplay second = f.Vm.Requests[1];
        second.Filled.Should().BeTrue();
        second.FilledText.Should().Be("Done for today");
        second.CanDeliver.Should().BeFalse("a request is filled once a day");
    }

    [Fact]
    public void Requests_InVietnamese_ShowTheVietnameseTexts()
    {
        using CultureScope scope = new("vi");
        Fixture f = Fixture.Create(parsnips: 0);

        f.Vm.Open();

        RequestDisplay first = f.Vm.Requests[0];
        first.AskText.Should().StartWith("Giao 5 × ");
        first.RewardText.Should().Contain("xu");
        first.CanDeliver.Should().BeFalse("the player carries nothing");
    }

    [Fact]
    public void Requests_CarryingTooFew_CannotDeliver()
    {
        Fixture f = Fixture.Create(parsnips: 4);

        f.Vm.Open();

        f.Vm.Requests[0].CanDeliver.Should().BeFalse();
    }

    [Fact]
    public void Requests_BeforeTheServerSpoke_AreEmptyAndNotLoaded()
    {
        Fixture f = Fixture.Create(parsnips: 0);

        f.Vm.Requests.Should().BeEmpty();
        f.Vm.IsLoaded.Should().BeFalse();
        f.Vm.NextText.Should().BeEmpty();
    }

    [Fact]
    public void NextText_ShowsTheTimeUntilTheNextRequests()
    {
        using CultureScope scope = new("en");
        Fixture f = Fixture.Create(parsnips: 0);

        f.Vm.Open();

        f.Vm.NextText.Should().Be("New requests in 1 h 30 min.");
    }

    [Fact]
    public void Deliver_SendsTheDayAndSlotTheBoardShowed_AndReadsTheBoardAgainOnSuccess()
    {
        using CultureScope scope = new("en");
        Fixture f = Fixture.Create(parsnips: 7);
        f.Vm.Open();

        f.Vm.Deliver(0);

        f.Delivered.Should().Equal((Day, 0));
        f.Vm.Status.Should().Be("Delivered! You received 130 coins.");
        f.Vm.StatusIsGood.Should().BeTrue();
        f.Socket.Requests.OfType<GetRequests>().Should().HaveCount(2, "the board is read when the tab opens and again after a delivery");
    }

    [Fact]
    public void BonusText_WhileTheEarlyWindowIsOpen_ShowsThePercentAndTheTimeLeft()
    {
        using CultureScope scope = new("en");
        Fixture f = Fixture.Create(parsnips: 0, earlyBonusOpen: true);

        f.Vm.Open();

        f.Vm.BonusText.Should().Be("Early bonus +20%: 0 h 30 min left.");
    }

    [Fact]
    public void BonusText_AfterTheEarlyWindow_IsEmpty()
    {
        Fixture f = Fixture.Create(parsnips: 0);

        f.Vm.Open();

        f.Vm.BonusText.Should().BeEmpty();
    }

    [Fact]
    public void Deliver_PaidTheEarlyBonus_SaysSo()
    {
        using CultureScope scope = new("en");
        Fixture f = Fixture.Create(parsnips: 7, earlyBonusOpen: true, bonusPaid: true);
        f.Vm.Open();

        f.Vm.Deliver(0);

        f.Vm.Status.Should().Be("Delivered early, with the bonus! You received 156 coins.");
        f.Vm.StatusIsGood.Should().BeTrue();
    }

    [Theory]
    [InlineData(RequestStatus.AlreadyFilled, "You already filled this request today.")]
    [InlineData(RequestStatus.NotAtBoard, "You are too far from the board.")]
    [InlineData(RequestStatus.NotEnough, "You do not carry enough of the item.")]
    public void Deliver_Refused_ShowsTheLocalizedReason(RequestStatus status, string expected)
    {
        using CultureScope scope = new("en");
        Fixture f = Fixture.Create(parsnips: 7, deliverStatus: status);
        f.Vm.Open();

        f.Vm.Deliver(0);

        f.Vm.Status.Should().Be(expected);
        f.Vm.StatusIsGood.Should().BeFalse();
    }

    [Fact]
    public void Deliver_ForADayThatEnded_ReadsTheBoardAgain()
    {
        Fixture f = Fixture.Create(parsnips: 7, deliverStatus: RequestStatus.NotToday);
        f.Vm.Open();

        f.Vm.Deliver(0);

        f.Socket.Requests.OfType<GetRequests>().Should().HaveCount(2);
    }

    [Fact]
    public void Deliver_BeforeTheBoardWasRead_SendsNothing()
    {
        Fixture f = Fixture.Create(parsnips: 7);

        f.Vm.Deliver(0);

        f.Delivered.Should().BeEmpty();
    }

    [Fact]
    public void Text_EveryRequestOutcomeAndLabelHasATextInBothLanguages()
    {
        List<string> language = [.. System.Enum.GetValues<RequestStatus>().Where(s => s != RequestStatus.Ok).Select(s => "Requests_Status_" + s)];
        language.AddRange(["Board_Tab_Goals", "Board_Tab_Requests", "Requests_Hint", "Requests_Loading", "Requests_None", "Requests_NewIn", "Requests_Ask", "Requests_Reward", "Requests_Held", "Requests_Deliver", "Requests_Filled", "Requests_Done", "Requests_Bonus", "Requests_DoneBonus", "Quest_Reward", "Hint_Quest"]);

        Keys("Language.resx").Should().Contain(language);
        Keys("Language.en.resx").Should().Contain(language);
        Keys("Language.resx").Should().BeEquivalentTo(Keys("Language.en.resx"));
    }

    // A view model on a fake socket: the server says it is day 42 with a parsnip request and a filled radish request; 90 minutes remain of the day.
    private sealed class Fixture
    {
        public required FakeGameSocket Socket { get; init; }

        public required RequestsViewModel Vm { get; init; }

        public required List<(int Day, int Slot)> Delivered { get; init; }

        public static Fixture Create(int parsnips, RequestStatus deliverStatus = RequestStatus.Ok, bool earlyBonusOpen = false, bool bonusPaid = false)
        {
            FakeGameSocket socket = new();
            List<(int, int)> delivered = [];
            socket.ReplyWith(_ =>
            {
                RequestsSnapshot snapshot = new()
                {
                    ServerNowMs = 1_000_000,
                    Day = Day,
                    DayEndsAtMs = 1_000_000 + (90 * 60_000L),
                    EarlyUntilMs = earlyBonusOpen ? 1_000_000 + (30 * 60_000L) : 0,
                    EarlyBonusPercent = 20,
                };
                snapshot.Rows.Add(new RequestRow { Slot = 0, ItemId = ItemCatalog.Parsnip, Quantity = 5, RewardCoins = 130, Filled = false });
                snapshot.Rows.Add(new RequestRow { Slot = 1, ItemId = ItemCatalog.Radish, Quantity = 3, RewardCoins = 117, Filled = true });
                return snapshot;
            });
            socket.ReplyWith<RequestResult>(request =>
            {
                DeliverRequest deliver = (DeliverRequest)request;
                delivered.Add((deliver.Day, deliver.Slot));
                return new RequestResult { Status = deliverStatus, Reward = deliverStatus == RequestStatus.Ok ? (bonusPaid ? 156 : 130) : 0, Bonus = bonusPaid };
            });
            socket.ReplyWith(_ => new CalendarInfo { EpochMs = 0, DayLengthMs = 43_200_000, ServerNowMs = 1_000_000 });

            InventoryChannel inventoryChannel = LoadedInventory(socket, parsnips > 0 ? [(5, ItemCatalog.Parsnip, parsnips)] : []);
            InventoryViewModel inventory = new(inventoryChannel, new FixedIcons(), Localizer<Nosum.Wasm.ViewModels.Resources.Catalogue>(), Text());
            CalendarChannel calendar = new(socket);
            calendar.Load();
            RequestsViewModel vm = new(new RequestChannel(socket), inventory, Describer(), calendar, Text());
            return new Fixture { Socket = socket, Vm = vm, Delivered = delivered };
        }

        private sealed class FixedIcons : IItemIconSource
        {
            public bool TryGet(int itemId, out ItemIcon icon)
            {
                icon = Icon;
                return true;
            }
        }
    }
}

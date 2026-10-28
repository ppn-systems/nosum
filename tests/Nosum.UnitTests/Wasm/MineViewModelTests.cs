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
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Market.S2C;
using Nosum.Contracts.Mine;
using Nosum.Contracts.Mine.C2S;
using Nosum.Contracts.Mine.S2C;
using Nosum.Contracts.Shop;
using Nosum.Contracts.Shop.C2S;
using Nosum.Contracts.Shop.S2C;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Blacksmith;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.Socket.Mine;
using Nosum.Wasm.ViewModels.Blacksmith;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Mine;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The blacksmith's upgrade section and the elevator picker (docs/adr/ADR-026): what they list, what they send and what they say about a refusal.</summary>
public sealed class MineViewModelTests
{
    private static readonly IReadOnlyList<(int Item, int Quantity)> s_basicTools =
    [
        (ItemCatalog.Hoe, 1), (ItemCatalog.WateringCan, 1), (ItemCatalog.ParsnipSeeds, 5), (ItemCatalog.Axe, 1), (ItemCatalog.Pickaxe, 1), (ItemCatalog.Scythe, 1),
    ];

    [Fact]
    public void Offers_AreTheUpgradableToolsTheCarrierHas_WithTheSharedPrice_AndNothingForTheScythe()
    {
        using CultureScope scope = new("en");
        Smith smith = Smith.Create(s_basicTools, coins: 0);

        IReadOnlyList<UpgradeOfferDisplay> offers = smith.Vm.Offers;

        offers.Select(o => o.ToolItemId).Should().Equal(ItemCatalog.Hoe, ItemCatalog.WateringCan, ItemCatalog.Axe, ItemCatalog.Pickaxe);
        offers.Should().OnlyContain(o => o.CostText.Contains("2,000 coins + 5", StringComparison.Ordinal) && o.CostText.EndsWith("Copper bar", StringComparison.Ordinal));
        offers.Should().OnlyContain(o => !o.CanUpgrade, "no coins, no bars");
        offers[0].To.ItemId.Should().Be(ItemCatalog.CopperHoe);
    }

    [Fact]
    public void Offers_CanUpgradeNeedsTheCoinsAndTheBars_AndAFreeSmith()
    {
        Smith poor = Smith.Create([.. s_basicTools, (ItemCatalog.CopperBar, 5)], coins: 1_999);
        Smith noBars = Smith.Create([.. s_basicTools, (ItemCatalog.CopperBar, 4)], coins: 2_000);
        Smith ready = Smith.Create([.. s_basicTools, (ItemCatalog.CopperBar, 5)], coins: 2_000);

        poor.Vm.Offers.Should().OnlyContain(o => !o.CanUpgrade);
        noBars.Vm.Offers.Should().OnlyContain(o => !o.CanUpgrade);
        ready.Vm.Offers.Should().OnlyContain(o => o.CanUpgrade);

        ready.Reply = new SmithAnswer(BlacksmithStatus.Ok, ItemCatalog.CopperHoe, 5, 3);
        ready.Vm.Open();
        ready.Vm.Offers.Should().OnlyContain(o => !o.CanUpgrade, "the smith has a tool of this player and takes one at a time");
    }

    [Fact]
    public void Offers_AreForTheBestTierCarried_IronBarsForTheSecondUpgrade_AndNothingForIronTools()
    {
        Smith smith = Smith.Create([(ItemCatalog.Hoe, 1), (ItemCatalog.CopperHoe, 1), (ItemCatalog.IronAxe, 1), (ItemCatalog.CopperPickaxe, 1), (ItemCatalog.IronBar, 7)], coins: 6_000);

        IReadOnlyList<UpgradeOfferDisplay> offers = smith.Vm.Offers;

        offers.Select(o => (o.ToolItemId, o.To.ItemId)).Should().Equal((ItemCatalog.CopperHoe, ItemCatalog.IronHoe), (ItemCatalog.CopperPickaxe, ItemCatalog.IronPickaxe));
        offers.Should().OnlyContain(o => o.CanUpgrade);
    }

    [Fact]
    public void Upgrade_SendsTheToolItem_AndShowsTheSmithsAnswer()
    {
        using CultureScope scope = new("en");
        Smith smith = Smith.Create([.. s_basicTools, (ItemCatalog.CopperBar, 5)], coins: 5_000);
        smith.Reply = new SmithAnswer(BlacksmithStatus.Ok, ItemCatalog.CopperHoe, 12, 10);

        smith.Vm.Upgrade(ItemCatalog.Hoe);

        ((UpgradeTool)smith.Socket.Requests.Last()).ItemId.Should().Be(ItemCatalog.Hoe);
        smith.Vm.Status.Should().Be("The smith has your tool.");
        smith.Vm.StatusIsGood.Should().BeTrue();
        smith.Vm.Job.Should().NotBeNull();
        smith.Vm.Job!.StatusText.Should().Be("Ready in 2 game days.");
        smith.Vm.Job.CanCollect.Should().BeFalse();

        smith.Reply = new SmithAnswer(BlacksmithStatus.NotEnoughBars, 0, 0, 10);
        smith.Vm.Upgrade(ItemCatalog.Axe);

        smith.Vm.Status.Should().Be("You do not have enough bars.");
        smith.Vm.StatusIsGood.Should().BeFalse();
    }

    [Theory]
    [InlineData(14, 12, "Ready! Take it back.", true)]
    [InlineData(12, 12, "Ready! Take it back.", true)]
    [InlineData(11, 12, "Ready tomorrow (game day).", false)]
    [InlineData(9, 12, "Ready in 3 game days.", false)]
    public void Job_SaysHowLongIsLeft_AndOnlyACompletedUpgradeCanBeCollected(long today, long readyDay, string text, bool canCollect)
    {
        using CultureScope scope = new("en");
        Smith smith = Smith.Create(s_basicTools, coins: 0);
        smith.Reply = new SmithAnswer(BlacksmithStatus.Ok, ItemCatalog.CopperAxe, readyDay, today);

        smith.Vm.Open();

        smith.Vm.Job!.StatusText.Should().Be(text);
        smith.Vm.Job.CanCollect.Should().Be(canCollect);
        smith.Vm.Job.Tool.ItemId.Should().Be(ItemCatalog.CopperAxe);
    }

    [Fact]
    public void Collect_SendsTheCollectRequest_AndTheSmithHoldsNothingAfterwards()
    {
        using CultureScope scope = new("en");
        Smith smith = Smith.Create(s_basicTools, coins: 0);
        smith.Reply = new SmithAnswer(BlacksmithStatus.Ok, 0, 0, 12);

        smith.Vm.Collect();

        smith.Socket.Requests.Last().Should().BeOfType<CollectTool>();
        smith.Vm.Status.Should().Be("You took your tool back.");
        smith.Vm.Job.Should().BeNull();
    }

    [Fact]
    public void Elevator_OpenReadsTheDepth_ListsTheEntranceAndEveryFifthFloorDownToIt_AndMarksWhereThePlayerIs()
    {
        using CultureScope scope = new("en");
        Lift lift = Lift.Create();
        lift.Reply = new LiftAnswer(MineStatus.Ok, 10, 12);

        lift.Vm.Open();

        lift.Socket.Requests.Last().Should().BeOfType<GetMine>();
        lift.Vm.IsOpen.Should().BeTrue();
        lift.Vm.Stops.Select(s => (s.Floor, s.Label, s.IsHere)).Should().Equal((0, "Mine entrance", false), (5, "Floor 5", false), (10, "Floor 10", true));
        lift.Vm.DeepestText.Should().Be("Deepest: floor 12");
    }

    [Fact]
    public void Elevator_Ride_SendsTheStop_ClosesWhenTheServerMovesThePlayer_AndStaysOpenWithTheReasonWhenItRefuses()
    {
        using CultureScope scope = new("en");
        Lift lift = Lift.Create();
        lift.Reply = new LiftAnswer(MineStatus.Ok, 0, 5);
        lift.Vm.Open();

        lift.Reply = new LiftAnswer(MineStatus.NotUnlocked, 0, 5);
        lift.Vm.Ride(10);

        ((RideElevator)lift.Socket.Requests.Last()).Floor.Should().Be(10);
        lift.Vm.IsOpen.Should().BeTrue();
        lift.Vm.Status.Should().Be("You have not been that deep.");

        lift.Reply = new LiftAnswer(MineStatus.Ok, 5, 5);
        lift.Vm.Ride(5);

        ((RideElevator)lift.Socket.Requests.Last()).Floor.Should().Be(5);
        lift.Vm.IsOpen.Should().BeFalse("the player is on their way");

        lift.Vm.Open();
        lift.Reply = new LiftAnswer(MineStatus.Ok, 0, 5);
        lift.Vm.RideHome();

        ((RideElevator)lift.Socket.Requests.Last()).Floor.Should().Be(Nosum.Gameplay.Mines.MineRules.ReturnHomeStop);
        lift.Vm.IsOpen.Should().BeFalse("returning home closes the elevator dialog");
    }

    [Fact]
    public void Elevator_CloseHidesThePicker()
    {
        Lift lift = Lift.Create();
        lift.Reply = new LiftAnswer(MineStatus.Ok, 0, 0);
        lift.Vm.Open();

        lift.Vm.Close();

        lift.Vm.IsOpen.Should().BeFalse();
        lift.Vm.Stops.Should().ContainSingle("only the entrance before the player has been anywhere");
    }

    [Fact]
    public void Language_EveryBlacksmithAndMineStatusAndTheMineTexts_ExistInBothLanguages()
    {
        List<string> keys = [.. Enum.GetValues<BlacksmithStatus>().Where(s => s != BlacksmithStatus.Ok).Select(s => "Smith_Status_" + s)];
        keys.AddRange(Enum.GetValues<MineStatus>().Where(s => s != MineStatus.Ok).Select(s => "Elevator_Status_" + s));
        keys.Add("Shop_Title_blacksmith");

        foreach (string file in new[] { "Language.resx", "Language.en.resx" })
        {
            Keys(file).Should().Contain(keys, file);
        }

        Keys("Language.resx").Where(k => k.StartsWith("Smith_", StringComparison.Ordinal) || k.StartsWith("Elevator_", StringComparison.Ordinal))
            .Should().BeEquivalentTo(Keys("Language.en.resx").Where(k => k.StartsWith("Smith_", StringComparison.Ordinal) || k.StartsWith("Elevator_", StringComparison.Ordinal)));
    }

    private sealed record SmithAnswer(BlacksmithStatus Status, int Tool, long Ready, long Today);

    private sealed record LiftAnswer(MineStatus Status, int Floor, int Deepest);

    private static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    private static HashSet<string> Keys(string file, [CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
        return XDocument.Load(Path.Combine(root, "wasm", "Nosum.Wasm.ViewModels", "Resources", file)).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public CultureScope(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }

    private sealed class NoIcons : IItemIconSource
    {
        public bool TryGet(int itemId, out ItemIcon icon)
        {
            icon = new ItemIcon("sheet.png", 0, 0, 16, 64, 64);
            return true;
        }
    }

    private sealed class Smith
    {
        private Smith(BlacksmithViewModel vm, FakeGameSocket socket)
        {
            this.Vm = vm;
            this.Socket = socket;
        }

        public BlacksmithViewModel Vm { get; }

        public FakeGameSocket Socket { get; }

        public SmithAnswer Reply { get; set; } = new(BlacksmithStatus.Ok, 0, 0, 0);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The channels live as long as the test.")]
        public static Smith Create(IReadOnlyList<(int Item, int Quantity)> carried, long coins)
        {
            FakeGameSocket socket = new();
            Smith smith = null!;
            socket.ReplyWith(_ => new BlacksmithState { Status = smith.Reply.Status, ToolItemId = smith.Reply.Tool, ReadyDay = smith.Reply.Ready, Today = smith.Reply.Today });
            socket.ReplyWith(_ =>
            {
                InventorySnapshot snapshot = new() { Capacity = (byte)36, SelectedSlot = 0 };
                for (int i = 0; i < carried.Count; i++)
                {
                    snapshot.Slots.Add(new InventorySlotRow { Slot = (byte)i, ItemId = carried[i].Item, Quantity = carried[i].Quantity });
                }

                return snapshot;
            });
            socket.ReplyWith(_ => new WalletSnapshot { Coins = coins });
            InventoryChannel inventory = new(socket);
            WalletChannel wallet = new(socket);
            inventory.Load();
            wallet.Load();
            ItemDescriber describer = new(new NoIcons(), Localizer<Catalogue>(), Localizer<Language>());
            BlacksmithViewModel vm = new(new BlacksmithChannel(socket), inventory, wallet, describer, Localizer<Language>());
            smith = new Smith(vm, socket);
            return smith;
        }
    }

    private sealed class Lift
    {
        private Lift(ElevatorViewModel vm, FakeGameSocket socket)
        {
            this.Vm = vm;
            this.Socket = socket;
        }

        public ElevatorViewModel Vm { get; }

        public FakeGameSocket Socket { get; }

        public LiftAnswer Reply { get; set; } = new(MineStatus.Ok, 0, 0);

        public static Lift Create()
        {
            FakeGameSocket socket = new();
            Lift lift = null!;
            socket.ReplyWith(_ => new MineState { Status = lift.Reply.Status, Floor = lift.Reply.Floor, Deepest = lift.Reply.Deepest });
            ElevatorViewModel vm = new(new MineChannel(socket), Localizer<Language>());
            lift = new Lift(vm, socket);
            return lift;
        }
    }
}

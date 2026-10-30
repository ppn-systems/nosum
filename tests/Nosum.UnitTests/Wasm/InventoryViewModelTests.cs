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
using Nosum.Contracts.Farm;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Farm.S2C;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Quests;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Placeable;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Node;
using Nosum.Contracts.Node.S2C;
using Nosum.Wasm.ViewModels.Feedback;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

[Collection(SharedUiCulture.Name)]
public sealed class InventoryViewModelTests
{
    private static readonly ItemIcon s_icon = new("sheet.png", 16, 32, 16, 16, 64, 64);

    [Theory]
    [InlineData("en", "Hoe")]
    [InlineData("vi", "Cuốc")]
    public void Slots_GetTheirNamesFromTheCatalogueInTheCurrentLanguage(string culture, string expected)
    {
        using CultureScope scope = new(culture);
        (InventoryViewModel vm, _, _) = Create();

        SlotDisplay hoe = vm.Slots[0];

        hoe.Name.Should().Be(expected);
        hoe.Tooltip.Should().StartWith(expected);
        hoe.Icon.Should().Be(s_icon);
    }

    [Fact]
    public void Slots_StackedItems_ShowTheQuantityInTheTooltipAndSingleItemsDoNot()
    {
        using CultureScope scope = new("en");
        (InventoryViewModel vm, _, _) = Create();

        vm.Slots[2].Tooltip.Should().Contain("Quantity: 8");
        vm.Slots[0].Tooltip.Should().NotContain("Quantity");
    }

    [Fact]
    public void Slots_EmptyOnesHaveNoTextAndTheHeldOneIsMarked()
    {
        (InventoryViewModel vm, _, _) = Create();

        vm.Slots.Should().HaveCount(36);
        vm.Slots[10].IsEmpty.Should().BeTrue();
        vm.Slots[10].Tooltip.Should().BeEmpty();
        vm.Slots.Where(s => s.IsSelected).Select(s => s.Index).Should().Equal(1);
        vm.Hotbar.Should().HaveCount(9);
    }

    [Fact]
    public void Select_ChangesTheHeldSlotAndTellsTheView()
    {
        (InventoryViewModel vm, InventoryChannel channel, FakeGameSocket socket) = Create();
        socket.ReplyWith(_ => new InventoryResult { Status = InventoryStatus.Ok, SelectedSlot = 2 });
        int changes = 0;
        vm.Changed += () => changes++;

        vm.Select(2);

        vm.SelectedSlot.Should().Be(2);
        vm.Slots[2].IsSelected.Should().BeTrue();
        changes.Should().BeGreaterThan(0);
        channel.SelectedSlot.Should().Be(2);
    }

    [Fact]
    public void Click_PickUpThenAnotherSlot_AsksTheServerToMove()
    {
        (InventoryViewModel vm, _, FakeGameSocket socket) = Create();
        socket.ReplyWith(_ => new InventoryResult { Status = InventoryStatus.Ok });

        vm.Click(2);
        vm.MovingFrom.Should().Be(2);
        vm.Click(20);

        vm.MovingFrom.Should().Be(-1);
        MoveSlot sent = socket.Requests.OfType<MoveSlot>().Should().ContainSingle().Subject;
        (sent.From, sent.To).Should().Be(((byte)2, (byte)20));
    }

    [Fact]
    public void Click_AnEmptySlotFirst_PicksNothingAndSameSlotTwiceLetsGo()
    {
        (InventoryViewModel vm, _, FakeGameSocket socket) = Create();

        vm.Click(15);
        vm.MovingFrom.Should().Be(-1);
        vm.Click(0);
        vm.Click(0);

        vm.MovingFrom.Should().Be(-1);
        socket.Requests.OfType<MoveSlot>().Should().BeEmpty();
    }

    [Fact]
    public void Refused_ARefusedMove_ReachesTheViewAsLocalizedText()
    {
        using CultureScope scope = new("en");
        (InventoryViewModel vm, _, FakeGameSocket socket) = Create();
        socket.ReplyWith(_ => new InventoryResult { Status = InventoryStatus.Blocked });
        List<string> texts = [];
        vm.Refused += texts.Add;

        vm.Click(2);
        vm.Click(0);

        texts.Should().Equal("Another item is in the way.");
    }

    [Fact]
    public void GameMessages_ARefusedFarmAction_ShowsItsTextForAMoment()
    {
        using CultureScope scope = new("en");
        (InventoryViewModel inventory, _, FakeGameSocket socket) = Create();
        using FarmChannel farm = new(socket);
        using PlaceableChannel placeable = new(socket);
        using GameMessagesViewModel messages = new(farm, placeable, inventory, Localizer<Language>());
        socket.ReplyWith(_ => new UseItemResult { Status = FarmStatus.NotTilled });

        farm.Use(2, 1, 1);

        messages.Message.Should().Be("Till the soil first.");
    }

    [Fact]
    public void GameMessages_ASuccessfulAction_ShowsNothing()
    {
        (InventoryViewModel inventory, _, FakeGameSocket socket) = Create();
        using FarmChannel farm = new(socket);
        using PlaceableChannel placeable = new(socket);
        using GameMessagesViewModel messages = new(farm, placeable, inventory, Localizer<Language>());
        socket.ReplyWith(_ => new UseItemResult { Status = FarmStatus.Ok, Action = FarmAction.Tilled });

        farm.Use(0, 1, 1);

        messages.Message.Should().BeNull();
    }

    [Fact]
    public void Catalogue_EveryItemAndQuest_HasNameDescriptionAndTextInBothLanguages()
    {
        HashSet<string> vi = Keys("Catalogue.resx");
        HashSet<string> en = Keys("Catalogue.en.resx");
        List<string> needed = [];
        foreach (ItemDefinition item in ItemCatalog.All)
        {
            needed.Add($"Item_{item.Key}_Name");
            needed.Add($"Item_{item.Key}_Description");
        }

        foreach (QuestDefinition quest in QuestCatalog.All)
        {
            needed.Add($"Quest_{quest.Key}_Title");
            needed.Add($"Quest_{quest.Key}_Hint");
            needed.Add($"Quest_{quest.Key}_Tip");
            for (int i = 0; i < quest.ObjectiveCount; i++)
            {
                needed.Add($"Quest_{quest.Key}_Objective_{i}");
            }
        }

        vi.Should().Contain(needed);
        en.Should().Contain(needed);
        vi.Should().BeEquivalentTo(en, "both languages carry the same keys");
    }

    [Fact]
    public void Language_EveryFarmStatusHasATextInBothLanguages()
    {
        string[] needed = Enum.GetValues<FarmStatus>().Where(s => s != FarmStatus.Ok).Select(s => "Farm_Status_" + s).ToArray();

        Keys("Language.resx", "Nosum.Wasm.ViewModels").Should().Contain(needed);
        Keys("Language.en.resx", "Nosum.Wasm.ViewModels").Should().Contain(needed);
    }

    [Fact]
    public void Language_EveryPlaceStatusHasATextInBothLanguages()
    {
        string[] needed = Enum.GetValues<PlaceStatus>().Where(s => s != PlaceStatus.Ok).Select(s => "Place_Status_" + s).ToArray();

        Keys("Language.resx", "Nosum.Wasm.ViewModels").Should().Contain(needed);
        Keys("Language.en.resx", "Nosum.Wasm.ViewModels").Should().Contain(needed);
    }

    [Fact]
    public void GameMessages_ARefusedPlacement_ShowsItsText()
    {
        using CultureScope scope = new("en");
        (InventoryViewModel inventory, _, FakeGameSocket socket) = Create();
        using FarmChannel farm = new(socket);
        using PlaceableChannel placeable = new(socket);
        using GameMessagesViewModel messages = new(farm, placeable, inventory, Localizer<Language>());
        socket.ReplyWith(_ => new PlaceResult { Status = PlaceStatus.Overlaps });

        placeable.Place(5, 3, 3, 0);

        messages.Message.Should().Be("There is furniture there already.");
    }

    [Fact]
    public void WateringCanSlot_ShowsTheWaterLeft_AndFollowsTheServer()
    {
        using CultureScope scope = new("en");
        (_, InventoryChannel channel, FakeGameSocket socket) = Create();
        using NodeChannel nodes = new(socket);
        using InventoryViewModel vm = new(channel, new FixedIcons(), Localizer<Catalogue>(), Localizer<Language>(), nodes);

        vm.Slots[1].Meter.Should().Be(1.0);
        vm.Slots[1].Tooltip.Should().Contain("Water: 40/40");

        using ToolState state = new() { WaterLeft = 10, WaterCapacity = 40 };
        socket.Push(state);

        vm.Slots[1].Meter.Should().Be(0.25);
        vm.Slots[1].MeterText.Should().Be("10");
        vm.Slots[0].Meter.Should().BeNull("only the watering can holds water");
    }

    [Fact]
    public void GameMessages_ASwingThatCameTooSoon_IsDroppedSilently_ButAWrongToolIsExplained()
    {
        using CultureScope scope = new("en");
        (InventoryViewModel inventory, _, FakeGameSocket socket) = Create();
        using FarmChannel farm = new(socket);
        using PlaceableChannel placeable = new(socket);
        using GameMessagesViewModel messages = new(farm, placeable, inventory, Localizer<Language>());

        socket.ReplyWith(_ => new UseItemResult { Status = FarmStatus.Cooldown });
        farm.Use(0, 3, 3);
        messages.Message.Should().BeNull();

        socket.ReplyWith(_ => new UseItemResult { Status = FarmStatus.WrongTool });
        farm.Use(0, 3, 3);
        messages.Message.Should().Be("This tool does not work on that.");
    }

    private static (InventoryViewModel Vm, InventoryChannel Channel, FakeGameSocket Socket) Create()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            InventorySnapshot snapshot = new() { Capacity = 36, SelectedSlot = 1 };
            snapshot.Slots.Add(new InventorySlotRow { Slot = 0, ItemId = ItemCatalog.Hoe, Quantity = 1 });
            snapshot.Slots.Add(new InventorySlotRow { Slot = 1, ItemId = ItemCatalog.WateringCan, Quantity = 1 });
            snapshot.Slots.Add(new InventorySlotRow { Slot = 2, ItemId = ItemCatalog.ParsnipSeeds, Quantity = 8 });
            return snapshot;
        });
        InventoryChannel channel = new(socket);
        InventoryViewModel vm = new(channel, new FixedIcons(), Localizer<Catalogue>(), Localizer<Language>());
        channel.Load();
        return (vm, channel, socket);
    }

    private static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    private static HashSet<string> Keys(string file, string project = "Nosum.Wasm.ViewModels", [CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
        string path = Path.Combine(root, "wasm", project, "Resources", file);
        return XDocument.Load(path).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
    }

    private sealed class FixedIcons : IItemIconSource
    {
        public bool TryGet(int itemId, out ItemIcon icon)
        {
            icon = s_icon;
            return true;
        }
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public CultureScope(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}

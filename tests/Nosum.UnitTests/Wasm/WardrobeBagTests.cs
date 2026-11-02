// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Looks;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures over a fake socket that dies with the test.")]
public sealed class WardrobeBagTests
{
    private static readonly int[] s_clothingSlots = [2, 7];

    [Fact]
    public void Wardrobe_ListsTheClothingTheBagHolds_WithTheirInventorySlots()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            InventorySnapshot snapshot = new() { Capacity = 36, SelectedSlot = 0 };
            snapshot.Slots.Add(new InventorySlotRow { Slot = 2, ItemId = ItemCatalog.LeatherArmor, Quantity = 1 });
            snapshot.Slots.Add(new InventorySlotRow { Slot = 5, ItemId = ItemCatalog.Parsnip, Quantity = 4 });
            snapshot.Slots.Add(new InventorySlotRow { Slot = 7, ItemId = ItemCatalog.IronChestplate, Quantity = 1 });
            return snapshot;
        });
        using FakeLookHost host = new(socket);
        host.Inventory.Load();

        IReadOnlyList<Nosum.Wasm.ViewModels.Inventory.SlotDisplay> bag = host.Wardrobe.Wardrobe;

        bag.Select(slot => slot.Index).Should().Equal(s_clothingSlots, "only clothing is listed, each at its inventory slot");
        bag.Select(slot => slot.ItemId).Should().Equal(ItemCatalog.LeatherArmor, ItemCatalog.IronChestplate);
    }

    private sealed class FakeLookHost : IDisposable
    {
        private readonly LookChannel _looks;
        private readonly InventoryChannel _channel;
        private readonly InventoryViewModel _inventory;

        public FakeLookHost(FakeGameSocket socket)
        {
            _looks = new LookChannel(socket, () => 1);
            _channel = new InventoryChannel(socket);
            ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
            StringLocalizer<Language> text = new(factory);
            StringLocalizer<Catalogue> catalogue = new(factory);
            _inventory = new InventoryViewModel(_channel, new NoIcons(), catalogue, text);
            Wardrobe = new WardrobeViewModel(_looks, _inventory, new ItemDescriber(new NoIcons(), catalogue, text), text);
        }

        public InventoryChannel Inventory => _channel;

        public WardrobeViewModel Wardrobe { get; }

        public void Dispose()
        {
            Wardrobe.Dispose();
            _inventory.Dispose();
            _channel.Dispose();
            _looks.Dispose();
        }

        private sealed class NoIcons : IItemIconSource
        {
            public bool TryGet(int itemId, out ItemIcon icon)
            {
                icon = default!;
                return false;
            }
        }
    }
}

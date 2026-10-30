// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.UnitTests.Wasm;

/// <summary>Helpers the crafting, chest and furnace tests share: real localizers over the real resources, the keys of a resource file, a culture scope and a loaded inventory.</summary>
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the fake socket owns nothing, pooled packets and channels die with the test.")]
internal static class WasmTestSupport
{
    /// <summary>The icon every item gets in tests.</summary>
    public static readonly ItemIcon Icon = new("sheet.png", 0, 0, 16, 16, 64, 64);

    public static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    public static StringLocalizer<Language> Text() => Localizer<Language>();

    public static ItemDescriber Describer() => new(new FixedIcons(), Localizer<Catalogue>(), Text());

    public static HashSet<string> Keys(string file, [CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
        string path = Path.Combine(root, "wasm", "Nosum.Wasm.ViewModels", "Resources", file);
        return XDocument.Load(path).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
    }

    /// <summary>Loads an inventory of 36 slots with the given stacks (slot, item, quantity) through a fake socket.</summary>
    public static InventoryChannel LoadedInventory(FakeGameSocket socket, params (int Slot, int ItemId, int Quantity)[] stacks)
    {
        socket.ReplyWith(_ =>
        {
            InventorySnapshot snapshot = new() { Capacity = 36, SelectedSlot = 0 };
            foreach ((int slot, int itemId, int quantity) in stacks)
            {
                snapshot.Slots.Add(new InventorySlotRow { Slot = (byte)slot, ItemId = itemId, Quantity = quantity });
            }

            return snapshot;
        });
        InventoryChannel channel = new(socket);
        channel.Load();
        return channel;
    }

    private sealed class FixedIcons : IItemIconSource
    {
        public bool TryGet(int itemId, out ItemIcon icon)
        {
            icon = Icon;
            return true;
        }
    }

    /// <summary>Sets the UI culture for a test and puts it back.</summary>
    public sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public CultureScope(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}

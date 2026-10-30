// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Journal.S2C;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Journal;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Journal;

/// <summary>
/// The collection journal (the J key or the menu): every item of the catalogue as a slot, the ones the farmer has obtained shown with their name,
/// count and first-obtained date, the rest as silhouettes with no name. The server decides what counts as obtained; this view model only shows it.
/// </summary>
public sealed class JournalViewModel : PanelViewModel
{
    private readonly JournalChannel _channel;
    private readonly IItemIconSource _icons;
    private readonly IStringLocalizer<Catalogue> _catalogue;

    /// <summary>Initializes the view model and follows the journal channel.</summary>
    /// <param name="text">UI text.</param>
    /// <param name="catalogue">Item names, by key.</param>
    /// <param name="icons">Item pictures.</param>
    /// <param name="channel">The server's journal.</param>
    public JournalViewModel(IStringLocalizer<Language> text, IStringLocalizer<Catalogue> catalogue, IItemIconSource icons, JournalChannel channel)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(channel);
        _catalogue = catalogue;
        _icons = icons;
        _channel = channel;
        _channel.Changed += () => this.Raise();
    }

    /// <summary>Gets how many items the catalogue has, discovered or not.</summary>
    public static int Total => ItemCatalog.All.Count;

    /// <summary>Gets how many of the catalogue's items the farmer has obtained.</summary>
    public int Discovered
    {
        get
        {
            int count = 0;
            IReadOnlyList<JournalEntry> entries = _channel.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (ItemCatalog.TryGet(entries[i].ItemId, out _))
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Asks the server for the journal. Call when the panel opens.</summary>
    public void Open() => _channel.Load();

    /// <summary>Builds one cell per catalogue item, in catalogue order.</summary>
    /// <returns>The cells; <see cref="JournalCell.Discovered"/> tells whether the farmer has the item.</returns>
    public List<JournalCell> Cells()
    {
        Dictionary<int, JournalEntry> found = new();
        IReadOnlyList<JournalEntry> entries = _channel.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            found[entries[i].ItemId] = entries[i];
        }

        IReadOnlyList<ItemDefinition> items = ItemCatalog.All;
        List<JournalCell> cells = new(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            ItemDefinition item = items[i];
            bool has = found.TryGetValue(item.Id, out JournalEntry? entry);
            string name = has ? _catalogue["Item_" + item.Key + "_Name"].Value : this.Text("Journal_Unknown");
            ItemIcon? picture = null;
            if (_icons.TryGet(item.Id, out ItemIcon icon))
            {
                picture = icon;
            }

            string tip = has
                ? this.Text("Journal_Tip", name, entry!.Count, FormatDate(entry.FirstObtainedUnix))
                : this.Text("Journal_Undiscovered");
            cells.Add(new JournalCell(new SlotDisplay(i, item.Id, has ? entry!.Count : 0, name, tip, picture, false), has));
        }

        return cells;
    }

    private static string FormatDate(long unixSeconds)
        => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
}

/// <summary>One journal slot: the item's display and whether the farmer has obtained it.</summary>
/// <param name="Slot">The slot to draw (a silhouette and no count when not discovered).</param>
/// <param name="Discovered">Whether the farmer has the item.</param>
public sealed record JournalCell(SlotDisplay Slot, bool Discovered);

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
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

    /// <summary>The cells on one page: five whole rows of eight.</summary>
    public const int CellsPerPage = 40;

    /// <summary>Gets the zero-based page the journal shows.</summary>
    public int Page { get; private set; }

    /// <summary>Gets which items the grid shows: every item, only the obtained ones, or only the missing ones (docs/design/critique-npc-hud.md).</summary>
    public JournalFilter Filter { get; private set; }

    /// <summary>Gets the number of pages of the cells the filter shows (at least one).</summary>
    public int PageCount => Math.Max(1, (this.FilteredCells().Count + CellsPerPage - 1) / CellsPerPage);

    /// <summary>Gets the cells on the current page, after the filter.</summary>
    public List<JournalCell> PageCells()
    {
        int page = Math.Min(this.Page, this.PageCount - 1);
        return this.FilteredCells().Skip(page * CellsPerPage).Take(CellsPerPage).ToList();
    }

    /// <summary>Shows another page of the journal.</summary>
    /// <param name="page">The zero-based page; clamped to the pages there are.</param>
    public void GoToPage(int page)
    {
        int next = Math.Clamp(page, 0, this.PageCount - 1);
        if (next != this.Page)
        {
            this.Page = next;
            this.Raise();
        }
    }

    /// <summary>Shows only the items the filter names, from the first page. Local to the panel: nothing is asked of the server.</summary>
    /// <param name="filter">The filter.</param>
    public void SetFilter(JournalFilter filter)
    {
        if (filter != this.Filter)
        {
            this.Filter = filter;
            this.Page = 0;
            this.Raise();
        }
    }

    /// <summary>Gets the cells the filter keeps, in catalogue order.</summary>
    /// <returns>The cells, every one of them when the filter is <see cref="JournalFilter.All"/>.</returns>
    public List<JournalCell> FilteredCells()
    {
        List<JournalCell> cells = this.Cells();
        if (this.Filter == JournalFilter.All)
        {
            return cells;
        }

        bool wantFound = this.Filter == JournalFilter.Found;
        return cells.FindAll(cell => cell.Discovered == wantFound);
    }

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

/// <summary>Which journal cells the grid shows.</summary>
public enum JournalFilter
{
    /// <summary>Every item of the catalogue.</summary>
    All = 0,

    /// <summary>Only the items the farmer has obtained.</summary>
    Found = 1,

    /// <summary>Only the items the farmer has not obtained yet.</summary>
    Missing = 2,
}

/// <summary>One journal slot: the item's display and whether the farmer has obtained it.</summary>
/// <param name="Slot">The slot to draw (a silhouette and no count when not discovered).</param>
/// <param name="Discovered">Whether the farmer has the item.</param>
public sealed record JournalCell(SlotDisplay Slot, bool Discovered);

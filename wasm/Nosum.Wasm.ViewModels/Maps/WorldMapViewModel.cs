// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Maps;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Maps;

/// <summary>
/// The world map panel (Tab or the HUD button): the public maps as cards, the one the farmer stands on highlighted, and for each the maps it
/// connects to and how many shops it has. Shop opening hours are not in the game data yet, so the card says so rather than inventing them.
/// </summary>
public sealed class WorldMapViewModel : PanelViewModel
{
    private string _currentMapId = GameMaps.DefaultId;

    /// <summary>Initializes the view model.</summary>
    /// <param name="text">UI text.</param>
    public WorldMapViewModel(IStringLocalizer<Language> text)
        : base(text)
    {
    }

    /// <summary>The public maps the world map shows, in the order of the cards: the farm, the village, the coast, the woods, the mountain and the mine entrance.</summary>
    public static IReadOnlyList<string> PublicMapIds { get; } =
    [
        GameMaps.Farm,
        GameMaps.Town,
        GameMaps.Beach,
        GameMaps.Woods,
        GameMaps.Mountain,
        GameMaps.Mine,
    ];

    /// <summary>Gets or sets the map the farmer is on; its card is highlighted.</summary>
    public string CurrentMapId
    {
        get => _currentMapId;
        set
        {
            if (_currentMapId != value)
            {
                _currentMapId = value;
                this.Raise();
            }
        }
    }

    /// <summary>
    /// Gets or sets the maps a map id connects to, set once by the page that holds the map loader. It returns <see langword="null"/> while
    /// that map is still loading; its card then shows that its connections are not known yet.
    /// </summary>
    public Func<string, IReadOnlyList<string>?> ConnectionsOf { get; set; } = static _ => null;

    /// <summary>The cards on one page: two to a page, one column, so a page fits the panel at 1280×720 and up (docs/design/ui-standard.md section 7).</summary>
    public const int CardsPerPage = 2;

    /// <summary>Gets the zero-based page shown.</summary>
    public int Page { get; private set; }

    /// <summary>Gets the number of pages (at least one).</summary>
    public static int PageCount => Math.Max(1, (PublicMapIds.Count + CardsPerPage - 1) / CardsPerPage);

    /// <summary>Shows another page of the map cards.</summary>
    /// <param name="page">The zero-based page; clamped to the pages there are.</param>
    public void GoToPage(int page)
    {
        int next = Math.Clamp(page, 0, PageCount - 1);
        if (next != this.Page)
        {
            this.Page = next;
            this.Raise();
        }
    }

    /// <summary>Builds the cards of the page shown.</summary>
    /// <returns>At most <see cref="CardsPerPage"/> cards.</returns>
    public List<WorldMapCard> PageCards()
    {
        int page = Math.Min(this.Page, PageCount - 1);
        List<WorldMapCard> all = this.Cards();
        return all.GetRange(page * CardsPerPage, Math.Min(CardsPerPage, all.Count - (page * CardsPerPage)));
    }

    /// <summary>Builds the cards from the map catalogue, one per public map.</summary>
    /// <returns>One card per public map.</returns>
    public List<WorldMapCard> Cards()
    {
        List<WorldMapCard> cards = new(PublicMapIds.Count);
        for (int i = 0; i < PublicMapIds.Count; i++)
        {
            string id = PublicMapIds[i];
            GameMaps.TryGet(id, out MapSpec? spec);
            IReadOnlyList<string>? links = this.ConnectionsOf(id);
            List<string> names = new();
            if (links is not null)
            {
                for (int j = 0; j < links.Count; j++)
                {
                    names.Add(this.NameOf(links[j]));
                }
            }

            cards.Add(new WorldMapCard(
                id,
                this.NameOf(id),
                string.Equals(id, _currentMapId, StringComparison.Ordinal),
                links is not null,
                names,
                spec?.Shops.Count ?? 0));
        }

        return cards;
    }

    /// <summary>Gets the localized name of a map id.</summary>
    /// <param name="mapId">The map id.</param>
    /// <returns>The name, for example "Town".</returns>
    public string NameOf(string mapId) => this.Text("Map_" + mapId + "_Name");
}

/// <summary>One world map card.</summary>
/// <param name="MapId">The map id.</param>
/// <param name="Name">The localized name.</param>
/// <param name="IsCurrent">Whether the farmer stands on this map.</param>
/// <param name="ConnectionsKnown">Whether the map is loaded, so its connections are known.</param>
/// <param name="Connections">The localized names of the maps it connects to.</param>
/// <param name="ShopCount">Shops on the map.</param>
public sealed record WorldMapCard(string MapId, string Name, bool IsCurrent, bool ConnectionsKnown, IReadOnlyList<string> Connections, int ShopCount);

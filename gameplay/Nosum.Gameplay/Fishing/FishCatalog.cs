// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Fishing;

/// <summary>
/// The ten fish species (docs/adr/ADR-071 D3). Ids are the fish item ids of <see cref="ItemCatalog"/>; prices are the item's
/// <c>SellPrice</c>. Seasons combine as <see cref="SeasonSet"/> flags.
/// </summary>
public static class FishCatalog
{
    private const SeasonSet Every = SeasonSet.Spring | SeasonSet.Summer | SeasonSet.Fall | SeasonSet.Winter;
    private const SeasonSet Warm = SeasonSet.Spring | SeasonSet.Summer;
    private const SeasonSet Cool = SeasonSet.Fall | SeasonSet.Winter;

    private static readonly FishSpecies[] s_all =
    [
        new(ItemCatalog.Anchovy, "anchovy", FishRarity.Common, 100, Every, FishingWater.Beach),
        new(ItemCatalog.SeaBass, "sea_bass", FishRarity.Common, 60, SeasonSet.Spring | SeasonSet.Summer | SeasonSet.Fall, FishingWater.Beach, FishTime.Day),
        new(ItemCatalog.Mackerel, "mackerel", FishRarity.Uncommon, 40, Cool, FishingWater.Beach),
        new(ItemCatalog.Pufferfish, "pufferfish", FishRarity.Rare, 12, SeasonSet.Summer, FishingWater.Beach),
        new(ItemCatalog.Carp, "carp", FishRarity.Common, 100, Every, FishingWater.Farm),
        new(ItemCatalog.Trout, "trout", FishRarity.Uncommon, 40, SeasonSet.Spring | SeasonSet.Fall, FishingWater.Woods, FishTime.Any, FishWeather.Waters),
        new(ItemCatalog.Bass, "bass", FishRarity.Uncommon, 35, SeasonSet.Summer | SeasonSet.Fall, FishingWater.Farm | FishingWater.Woods),
        new(ItemCatalog.Pike, "pike", FishRarity.Rare, 12, SeasonSet.Winter, FishingWater.Woods, FishTime.Night),
        new(ItemCatalog.Eel, "eel", FishRarity.Rare, 10, SeasonSet.Fall, FishingWater.Woods, FishTime.Night, FishWeather.Waters),
        new(ItemCatalog.GoldenKoi, "golden_koi", FishRarity.Legendary, 2, SeasonSet.Spring, FishingWater.Farm, FishTime.Any, FishWeather.Waters),
    ];

    private static readonly Dictionary<int, FishSpecies> s_byId = Index();

    /// <summary>Gets every species, in item id order.</summary>
    public static IReadOnlyList<FishSpecies> All => s_all;

    /// <summary>Looks up a species by its item id.</summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="species">The species when found.</param>
    /// <returns><see langword="true"/> when the item id is a fish.</returns>
    public static bool TryGet(int itemId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out FishSpecies? species) => s_byId.TryGetValue(itemId, out species);

    private static Dictionary<int, FishSpecies> Index()
    {
        Dictionary<int, FishSpecies> index = new(s_all.Length);
        foreach (FishSpecies species in s_all)
        {
            index.Add(species.ItemId, species);
        }

        return index;
    }
}

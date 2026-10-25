// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Farm;

/// <summary>
/// Every crop the game knows. The durations are watered game days per stage (docs/adr/ADR-021 D7; a game day is 12 real hours,
/// docs/adr/ADR-018): parsnip 1/1/1/1 = 4 days, radish 1/2/1/2 = 6, cauliflower 1/2/4/4/1 = 12. Balance is an open owner decision. Ids are append-only. Each crop lists the seasons it grows in (docs/adr/ADR-018): parsnip is the hardy
/// root (spring, fall and winter, so a new player in any season can plant the tutorial crop), radish grows in summer and fall,
/// cauliflower in spring and summer. The sets are product defaults, not balance.
/// </summary>
public static class CropCatalog
{
    /// <summary>Parsnip: five sprite stages, four watered days.</summary>
    public const int Parsnip = 1;

    /// <summary>Radish: five sprite stages, six watered days.</summary>
    public const int Radish = 2;

    /// <summary>Cauliflower: six sprite stages, the slowest: twelve watered days.</summary>
    public const int Cauliflower = 3;

    private static readonly CropDefinition[] s_all =
    [
        new(Parsnip, "parsnip", SeasonSet.Spring | SeasonSet.Fall | SeasonSet.Winter, ItemCatalog.Parsnip, 1, 1, 1, 1, 1),
        new(Radish, "radish", SeasonSet.Summer | SeasonSet.Fall, ItemCatalog.Radish, 1, 1, 2, 1, 2),
        new(Cauliflower, "cauliflower", SeasonSet.Spring | SeasonSet.Summer, ItemCatalog.Cauliflower, 1, 1, 2, 4, 4, 1),
    ];

    private static readonly Dictionary<int, CropDefinition> s_byId = Index();

    /// <summary>Gets every crop, in id order.</summary>
    public static IReadOnlyList<CropDefinition> All => s_all;

    /// <summary>Looks up a crop.</summary>
    /// <param name="cropId">Crop id.</param>
    /// <param name="crop">The definition when found.</param>
    /// <returns><see langword="true"/> when the id names a crop.</returns>
    public static bool TryGet(int cropId, [NotNullWhen(true)] out CropDefinition? crop) => s_byId.TryGetValue(cropId, out crop);

    private static Dictionary<int, CropDefinition> Index()
    {
        Dictionary<int, CropDefinition> index = new(s_all.Length);
        foreach (CropDefinition crop in s_all)
        {
            index.Add(crop.Id, crop);
        }

        return index;
    }
}

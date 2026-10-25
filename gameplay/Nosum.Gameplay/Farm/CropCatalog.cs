// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Farm;

/// <summary>
/// Every crop the game knows. The durations are short development values (seconds of watered
/// growth per stage) so the loop can be played and tested in minutes; real-play balance is an open
/// owner decision (docs/adr/ADR-011). Ids are append-only. Each crop lists the seasons it grows in (docs/adr/ADR-018): parsnip is the hardy
/// root (spring, fall and winter, so a new player in any season can plant the tutorial crop), radish grows in summer and fall,
/// cauliflower in spring and summer. The sets are product defaults, not balance.
/// </summary>
public static class CropCatalog
{
    /// <summary>Parsnip: five sprite stages, about a minute.</summary>
    public const int Parsnip = 1;

    /// <summary>Radish: five sprite stages, a little longer.</summary>
    public const int Radish = 2;

    /// <summary>Cauliflower: six sprite stages, the slowest and the only one that needs watering twice.</summary>
    public const int Cauliflower = 3;

    private static readonly CropDefinition[] s_all =
    [
        new(Parsnip, "parsnip", SeasonSet.Spring | SeasonSet.Fall | SeasonSet.Winter, ItemCatalog.Parsnip, 1, 12, 16, 16, 16),
        new(Radish, "radish", SeasonSet.Summer | SeasonSet.Fall, ItemCatalog.Radish, 1, 14, 18, 18, 20),
        new(Cauliflower, "cauliflower", SeasonSet.Spring | SeasonSet.Summer, ItemCatalog.Cauliflower, 1, 16, 20, 24, 24, 26),
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

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

    /// <summary>Potato.</summary>
    public const int Potato = 4;

    /// <summary>Green bean.</summary>
    public const int GreenBean = 5;

    /// <summary>Kale.</summary>
    public const int Kale = 6;

    /// <summary>Garlic.</summary>
    public const int Garlic = 7;

    /// <summary>Strawberry.</summary>
    public const int Strawberry = 8;

    /// <summary>Tomato.</summary>
    public const int Tomato = 9;

    /// <summary>Blueberry.</summary>
    public const int Blueberry = 10;

    /// <summary>Melon.</summary>
    public const int Melon = 11;

    /// <summary>Hot pepper.</summary>
    public const int HotPepper = 12;

    /// <summary>Corn.</summary>
    public const int Corn = 13;

    /// <summary>Wheat.</summary>
    public const int Wheat = 14;

    /// <summary>Sunflower.</summary>
    public const int Sunflower = 15;

    /// <summary>Pumpkin.</summary>
    public const int Pumpkin = 16;

    /// <summary>Cranberries.</summary>
    public const int Cranberries = 17;

    /// <summary>Eggplant.</summary>
    public const int Eggplant = 18;

    /// <summary>Yam.</summary>
    public const int Yam = 19;

    /// <summary>Bok choy.</summary>
    public const int BokChoy = 20;

    /// <summary>Grape.</summary>
    public const int Grape = 21;

    private static readonly CropDefinition[] s_all =
    [
        new(Parsnip, "parsnip", SeasonSet.Spring | SeasonSet.Fall | SeasonSet.Winter, ItemCatalog.Parsnip, 1, 1, 1, 1, 1) { SpriteRow = 0, SpriteFrames = [0, 2, 3, 4, 5] },
        new(Radish, "radish", SeasonSet.Spring | SeasonSet.Summer | SeasonSet.Fall, ItemCatalog.Radish, 1, 1, 2, 1, 2) { SpriteRow = 12, SpriteFrames = [0, 2, 3, 4, 5] },
        new(Cauliflower, "cauliflower", SeasonSet.Spring | SeasonSet.Summer, ItemCatalog.Cauliflower, 1, 1, 2, 4, 4, 1) { SpriteRow = 2, SpriteFrames = [0, 2, 3, 4, 5, 6] },
        new(Potato, "potato", SeasonSet.Spring, ItemCatalog.Potato, 1, 1, 1, 1, 2, 1) { SpriteRow = 3, SpriteFrames = [0, 2, 3, 4, 5, 6] },
        new(GreenBean, "green_bean", SeasonSet.Spring, ItemCatalog.GreenBean, 1, 1, 1, 1, 3, 4) { RegrowDays = 3, SpriteRow = 1, SpriteFrames = [0, 2, 3, 4, 7, 6] },
        new(Kale, "kale", SeasonSet.Spring, ItemCatalog.Kale, 1, 1, 2, 2, 1) { SpriteRow = 5, SpriteFrames = [0, 2, 3, 4, 5] },
        new(Garlic, "garlic", SeasonSet.Spring, ItemCatalog.Garlic, 1, 1, 1, 1, 1) { SpriteRow = 4, SpriteFrames = [0, 2, 3, 4, 5] },
        new(Strawberry, "strawberry", SeasonSet.Spring, ItemCatalog.Strawberry, 1, 1, 1, 2, 4) { RegrowDays = 4, SpriteRow = 36, SpriteFrames = [0, 2, 3, 7, 6] },
        new(Tomato, "tomato", SeasonSet.Summer, ItemCatalog.Tomato, 1, 2, 2, 2, 1, 4) { RegrowDays = 4, SpriteRow = 8, SpriteFrames = [0, 2, 3, 5, 7, 6] },
        new(Blueberry, "blueberry", SeasonSet.Summer, ItemCatalog.Blueberry, 3, 1, 3, 3, 2, 4) { RegrowDays = 4, SpriteRow = 9, SpriteFrames = [0, 2, 3, 4, 7, 6] },
        new(Melon, "melon", SeasonSet.Summer, ItemCatalog.Melon, 1, 1, 2, 3, 3, 3) { SpriteRow = 7, SpriteFrames = [0, 2, 3, 4, 5, 6] },
        new(HotPepper, "hot_pepper", SeasonSet.Summer, ItemCatalog.HotPepper, 1, 1, 1, 1, 3) { RegrowDays = 3, SpriteRow = 10, SpriteFrames = [0, 2, 3, 7, 6] },
        new(Corn, "corn", SeasonSet.Summer | SeasonSet.Fall, ItemCatalog.Corn, 1, 2, 3, 3, 2, 4) { RegrowDays = 4, SpriteRow = 15, SpriteFrames = [0, 2, 3, 5, 7, 6] },
        new(Wheat, "wheat", SeasonSet.Summer | SeasonSet.Fall, ItemCatalog.Wheat, 1, 1, 1, 1, 1) { SpriteRow = 11, SpriteFrames = [0, 2, 3, 4, 5] },
        new(Sunflower, "sunflower", SeasonSet.Summer, ItemCatalog.Sunflower, 1, 1, 2, 3, 2) { SpriteRow = 30, SpriteFrames = [0, 2, 3, 4, 5] },
        new(Pumpkin, "pumpkin", SeasonSet.Fall, ItemCatalog.Pumpkin, 1, 1, 2, 3, 4, 3) { SpriteRow = 18, SpriteFrames = [0, 2, 3, 4, 5, 6] },
        new(Cranberries, "cranberries", SeasonSet.Fall, ItemCatalog.Cranberries, 2, 1, 2, 2, 4) { RegrowDays = 4, SpriteRow = 21, SpriteFrames = [0, 2, 4, 7, 6] },
        new(Eggplant, "eggplant", SeasonSet.Fall, ItemCatalog.Eggplant, 1, 1, 1, 1, 2) { RegrowDays = 2, SpriteRow = 16, SpriteFrames = [0, 2, 3, 7, 6] },
        new(Yam, "yam", SeasonSet.Fall, ItemCatalog.Yam, 1, 1, 3, 3, 3) { SpriteRow = 20, SpriteFrames = [0, 2, 3, 4, 5] },
        new(BokChoy, "bok_choy", SeasonSet.Fall, ItemCatalog.BokChoy, 1, 1, 1, 1, 1) { SpriteRow = 19, SpriteFrames = [0, 2, 3, 4, 5] },
        new(Grape, "grape", SeasonSet.Fall, ItemCatalog.Grape, 1, 1, 2, 3, 4) { RegrowDays = 3, SpriteRow = 38, SpriteFrames = [0, 2, 4, 7, 6] },
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

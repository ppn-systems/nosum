// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;

namespace Nosum.Wasm.ViewModels.Feedback;

/// <summary>
/// Names the seasons a seed's crop grows in, for the line that answers a seed refused as out of season (docs/adr/ADR-121 D1). The server says only
/// that the crop does not grow now; the client already holds the crop's seasons (the same data the shop greys a row by), so the line can say when
/// it does. No wire change.
/// </summary>
public static class FarmSeasonNotice
{
    /// <summary>
    /// The seasons a seed's crop grows in, in calendar order.
    /// </summary>
    /// <param name="seedItemId">The item in hand; anything but a seed with a known crop gives an empty list.</param>
    /// <returns>The seasons, possibly empty.</returns>
    public static List<Season> SeasonsOf(int seedItemId)
    {
        List<Season> seasons = [];
        if (!ItemCatalog.TryGet(seedItemId, out ItemDefinition? item) || item.Kind != ItemKind.Seed || !CropCatalog.TryGet(item.CropId, out CropDefinition? crop))
        {
            return seasons;
        }

        foreach (Season season in Enum.GetValues<Season>())
        {
            if (crop.GrowsIn(season))
            {
                seasons.Add(season);
            }
        }

        return seasons;
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Contracts.Farm;
using Nosum.Gameplay.Items;

namespace Nosum.Wasm.ViewModels.Feedback;

/// <summary>
/// Decides when a farm use is named on the message line as a harvest made with another item in hand (docs/adr/ADR-095 D2). The server harvests a
/// ripe crop whatever is held (the action key works the crop), and a tap does the same, so the player is told the crop was harvested even when
/// the item in hand is a seed, a can or a hoe. The scythe is the harvesting tool, so its own harvest is not named.
/// </summary>
public static class FarmHarvestNotice
{
    /// <summary>
    /// Whether the reply of a use names a harvest made with another item in hand.
    /// </summary>
    /// <param name="status">The server's status for the use.</param>
    /// <param name="action">What the server did to the tile.</param>
    /// <param name="tiles">How many tiles the use covered; only a single-tile use is named.</param>
    /// <param name="heldItemId">The item in hand when the use was made, zero for none.</param>
    /// <returns><see langword="true"/> when the line should name the harvest.</returns>
    public static bool NamesHarvest(FarmStatus status, FarmAction action, int tiles, int heldItemId)
    {
        return status == FarmStatus.Ok
            && action == FarmAction.Harvested
            && tiles == 1
            && heldItemId is not (0 or ItemCatalog.Scythe);
    }
}

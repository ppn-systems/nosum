// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Contracts.Farm;

namespace Nosum.Wasm.ViewModels.Feedback;

/// <summary>
/// Chooses the message line for a farm use that worked (docs/adr/ADR-095 D2 and D4). Until now a successful till, sow, water or harvest was
/// silent: the player learned it worked from the tile and the quest card. The line is one short sentence per use; a harvest made with another
/// item in hand keeps its own line (<see cref="FarmHarvestNotice"/>).
/// </summary>
public static class FarmDoneNotice
{
    /// <summary>
    /// The resource key of the line for a use, or <see langword="null"/> when the use is silent (a refusal, a ladder, a hit or a swing that did nothing).
    /// </summary>
    /// <param name="status">The server's status for the use.</param>
    /// <param name="action">What the server did to the tile.</param>
    /// <param name="tiles">How many tiles the use covered.</param>
    /// <param name="heldItemId">The item in hand when the use was made, zero for none.</param>
    /// <returns>The key of the line, or <see langword="null"/>.</returns>
    public static string? KeyOf(FarmStatus status, FarmAction action, int tiles, int heldItemId)
    {
        if (status != FarmStatus.Ok)
        {
            return null;
        }

        if (FarmHarvestNotice.NamesHarvest(status, action, tiles, heldItemId))
        {
            return "Farm_HarvestedWithOtherItem";
        }

        return action switch
        {
            FarmAction.Tilled => "Farm_Done_Tilled",
            FarmAction.Planted => "Farm_Done_Planted",
            FarmAction.Watered => "Farm_Done_Watered",
            FarmAction.Harvested => "Farm_Done_Harvested",
            _ => null,
        };
    }
}

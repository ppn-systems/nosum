// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Coops;

/// <summary>
/// Where co-op land is entered and left, and the audit reason codes of farming on it (docs/adr/ADR-020).
/// Tile coordinates are those of the placeholder maps (docs/adr/ADR-007).
/// </summary>
public static class CoopLand
{
    /// <summary>Town tile that takes a member into their co-op's land: a gate on the west plaza, south of where the road from the bus stop arrives.</summary>
    public static readonly (int X, int Y) TownGate = (13, 60);

    /// <summary>Town tile a member arrives on when leaving co-op land, beside <see cref="TownGate"/> (not on it, so they do not bounce back in).</summary>
    public static readonly (int X, int Y) TownArrival = (12, 60);

    /// <summary>Tile of the co-op land where members arrive (open ground, not tillable).</summary>
    public static readonly (int X, int Y) LandArrival = (46, 22);

    /// <summary>Tile of the co-op land that leads back to Town, three tiles south of <see cref="LandArrival"/>.</summary>
    public static readonly (int X, int Y) LandGate = (46, 25);

    /// <summary>Audit reason of a hoe use on co-op land.</summary>
    public const string TillReason = "coop.farm.till";

    /// <summary>Audit reason of a seed planted on co-op land; the seed leaves the planter's inventory.</summary>
    public const string PlantReason = "coop.farm.plant";

    /// <summary>Audit reason of a watering on co-op land.</summary>
    public const string WaterReason = "coop.farm.water";

    /// <summary>Audit reason of a harvest on co-op land; the produce enters the harvester's inventory.</summary>
    public const string HarvestReason = "coop.farm.harvest";

    /// <summary>Audit reason of an object placed on co-op land; the item leaves the placer's inventory (docs/adr/ADR-024).</summary>
    public const string PlaceReason = "coop.place";

    /// <summary>Audit reason of an object picked up on co-op land; the item enters the player's inventory.</summary>
    public const string PickUpReason = "coop.pickup";

    /// <summary>Audit reason of ore and coal put into a furnace on co-op land; they leave the member's inventory (docs/adr/ADR-024).</summary>
    public const string SmeltReason = "coop.smelt";

    /// <summary>Audit reason of a bar collected from a furnace on co-op land; it enters the member's inventory.</summary>
    public const string CollectReason = "coop.collect";

    /// <summary>Gets whether an audit reason belongs to the warehouse (a deposit or a withdrawal), which is what a member's net contribution counts.</summary>
    /// <param name="reason">The log line's reason code.</param>
    /// <returns><see langword="true"/> for <c>coop.deposit</c> and <c>coop.withdraw</c>.</returns>
    public static bool IsWarehouseReason(string reason) => reason is "coop.deposit" or "coop.withdraw";

    /// <summary>Maps a farm-rule reason code (<c>farm.till</c> ...) to the co-op audit reason of the same action.</summary>
    /// <param name="farmReason">The reason the farm rules gave the action.</param>
    /// <returns>The co-op audit reason.</returns>
    public static string AuditReason(string farmReason) => farmReason switch
    {
        "farm.till" => TillReason,
        "farm.plant" => PlantReason,
        "farm.water" => WaterReason,
        _ => HarvestReason,
    };
}

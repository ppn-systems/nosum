// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Farm.S2C;

/// <summary>
/// One tilled tile. Carries the stored growth numbers, not a stage: the client computes the stage with the
/// same rules as the server (<c>FarmRules</c>), the server calendar and a server-synchronized clock, so crops grow on screen without a push per stage.
/// Growth counts watered game days (docs/adr/ADR-021 D7). Orders 4 to 6 are the retired millisecond growth fields of the first farm slice: the wire
/// is append-only, so they stay and are always zero; the next protocol version bump may remove them.
/// </summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class FarmTileRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static FarmTileRow Create() => new();

    /// <summary>Tile x.</summary>
    [SerializeOrder(0)] public ushort X { get; set; }

    /// <summary>Tile y.</summary>
    [SerializeOrder(1)] public ushort Y { get; set; }

    /// <summary>Planted crop id; zero for bare soil.</summary>
    [SerializeOrder(2)] public int CropId { get; set; }

    /// <summary>When the crop was planted.</summary>
    [SerializeOrder(3)] public long PlantedAtMs { get; set; }

    /// <summary>Retired (always zero): watered growth time of the first farm slice.</summary>
    [SerializeOrder(4)] public long GrowthMs { get; set; }

    /// <summary>Retired (always zero).</summary>
    [SerializeOrder(5)] public long SettledAtMs { get; set; }

    /// <summary>Retired (always zero).</summary>
    [SerializeOrder(6)] public long WetUntilMs { get; set; }

    /// <summary>Watered game days settled so far, not counting <see cref="WateredDay"/>.</summary>
    [SerializeOrder(7)] public int GrowthDays { get; set; }

    /// <summary>The last game day number (1-based since the calendar epoch) the soil was watered; zero when never.</summary>
    [SerializeOrder(8)] public int WateredDay { get; set; }

    /// <summary>True when the tile is no longer tilled (bare soil reverted): the client removes it and ignores the other fields.</summary>
    [SerializeOrder(9)] public bool Removed { get; set; }

    /// <summary>The game day the bare soil was tilled or its crop harvested or cut; with <see cref="WateredDay"/> it decides when bare soil reverts (docs/adr/ADR-021 D7).</summary>
    [SerializeOrder(10)] public int TendedDay { get; set; }
}

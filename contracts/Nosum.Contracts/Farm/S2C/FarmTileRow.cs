// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Farm.S2C;

/// <summary>
/// One tilled tile. Carries the stored timestamps, not a stage: the client computes the stage with the
/// same rules as the server and a server-synchronized clock, so crops grow on screen without a push per stage.
/// Times are Unix milliseconds on the server's clock.
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

    /// <summary>Watered growth time settled up to <see cref="SettledAtMs"/>.</summary>
    [SerializeOrder(4)] public long GrowthMs { get; set; }

    /// <summary>The moment <see cref="GrowthMs"/> was last settled.</summary>
    [SerializeOrder(5)] public long SettledAtMs { get; set; }

    /// <summary>The soil is wet until this moment.</summary>
    [SerializeOrder(6)] public long WetUntilMs { get; set; }
}

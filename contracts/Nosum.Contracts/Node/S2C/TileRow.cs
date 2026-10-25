// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Node.S2C;

/// <summary>A tile position.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TileRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static TileRow Create() => new();

    /// <summary>Tile x.</summary>
    [SerializeOrder(0)] public ushort X { get; set; }

    /// <summary>Tile y.</summary>
    [SerializeOrder(1)] public ushort Y { get; set; }
}

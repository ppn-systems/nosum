// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.GroundItem.S2C;

/// <summary>One item lying on the ground: what it is, how many, and where (pixels on the map).</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GroundItemRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static GroundItemRow Create() => new();

    /// <summary>Ground item id, unique across the game and never reused.</summary>
    [SerializeOrder(0)] public long Id { get; set; }

    /// <summary>The item; the client finds its picture by it.</summary>
    [SerializeOrder(1)] public int ItemId { get; set; }

    /// <summary>How many lie there.</summary>
    [SerializeOrder(2)] public int Quantity { get; set; }

    /// <summary>X on the map, pixels.</summary>
    [SerializeOrder(3)] public ushort X { get; set; }

    /// <summary>Y on the map, pixels.</summary>
    [SerializeOrder(4)] public ushort Y { get; set; }
}

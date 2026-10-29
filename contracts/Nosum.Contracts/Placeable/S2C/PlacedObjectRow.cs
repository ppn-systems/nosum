// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Placeable.S2C;

/// <summary>One placed object: what it is, where its footprint's top-left tile is, and how it is turned.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PlacedObjectRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static PlacedObjectRow Create() => new();

    /// <summary>Object id, unique across the game.</summary>
    [SerializeOrder(0)] public int Id { get; set; }

    /// <summary>The inventory item the object is made from; the client finds its art by it.</summary>
    [SerializeOrder(1)] public int ItemId { get; set; }

    /// <summary>Left tile of the footprint.</summary>
    [SerializeOrder(2)] public ushort TileX { get; set; }

    /// <summary>Top tile of the footprint.</summary>
    [SerializeOrder(3)] public ushort TileY { get; set; }

    /// <summary>Rotation: 0 down, 1 right, 2 up, 3 left.</summary>
    [SerializeOrder(4)] public byte Rotation { get; set; }

    /// <summary>When it was placed, Unix milliseconds; a sprinkler waters the crops beside it from the day after (docs/adr/ADR-024 D4).</summary>
    [SerializeOrder(5)] public long PlacedAtMs { get; set; }
}

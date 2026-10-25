// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.C2S;

/// <summary>Moves and/or rotates a placed object to a new position. Validated like <see cref="PlaceObject"/>; the object itself is ignored when checking overlap.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MoveObject : PacketBase<MoveObject>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.MoveObject;

    /// <summary>Id of the object, from <see cref="S2C.PlacedObjectRow.Id"/>.</summary>
    [SerializeOrder(0)] public int ObjectId { get; set; }

    /// <summary>New left tile of the footprint.</summary>
    [SerializeOrder(1)] public int TileX { get; set; }

    /// <summary>New top tile of the footprint.</summary>
    [SerializeOrder(2)] public int TileY { get; set; }

    /// <summary>New rotation.</summary>
    [SerializeOrder(3)] public byte Rotation { get; set; }
}

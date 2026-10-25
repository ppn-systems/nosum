// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.C2S;

/// <summary>
/// Places the item held in an inventory slot with its footprint's top-left tile at a position. An intent: the server checks
/// that the player is in their own place, owns the item, is within reach, and that the footprint is free floor.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PlaceObject : PacketBase<PlaceObject>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.PlaceObject;

    /// <summary>Inventory slot that holds the item.</summary>
    [SerializeOrder(0)] public byte Slot { get; set; }

    /// <summary>Left tile of the footprint.</summary>
    [SerializeOrder(1)] public int TileX { get; set; }

    /// <summary>Top tile of the footprint.</summary>
    [SerializeOrder(2)] public int TileY { get; set; }

    /// <summary>Rotation: 0 down, 1 right, 2 up, 3 left (pieces that do not rotate accept only 0).</summary>
    [SerializeOrder(3)] public byte Rotation { get; set; }
}

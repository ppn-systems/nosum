// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.C2S;

/// <summary>Picks a placed object up: it leaves the room and its item returns to the player's inventory in one step.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PickUpObject : PacketBase<PickUpObject>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.PickUpObject;

    /// <summary>Id of the object, from <see cref="S2C.PlacedObjectRow.Id"/>.</summary>
    [SerializeOrder(0)] public int ObjectId { get; set; }
}

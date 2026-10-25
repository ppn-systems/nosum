// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.C2S;

/// <summary>Takes the finished bar out of a furnace into the inventory. Reply: <see cref="S2C.FurnaceResult"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CollectFurnace : PacketBase<CollectFurnace>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.CollectFurnace;

    /// <summary>Id of the placed furnace.</summary>
    [SerializeOrder(0)] public int ObjectId { get; set; }
}

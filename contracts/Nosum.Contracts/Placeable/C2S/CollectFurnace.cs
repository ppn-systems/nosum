// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

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
    [WireRange(0, int.MaxValue)]
    [SerializeOrder(0)] public int ObjectId { get; set; }
}

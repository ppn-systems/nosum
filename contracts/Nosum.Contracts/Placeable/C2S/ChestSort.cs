// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Placeable.C2S;

/// <summary>Sorts an open chest: merges partial stacks and lays the stacks out by item kind and id. One atomic step; the server decides the layout. Reply: <see cref="S2C.ChestResult"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ChestSort : PacketBase<ChestSort>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.ChestSort;

    /// <summary>Id of the open chest.</summary>
    [WireRange(0, int.MaxValue)]
    [SerializeOrder(0)] public int ObjectId { get; set; }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.S2C;

/// <summary>Pushed to a viewer whose chest is gone or no longer theirs to look into (picked up, walked away, left the co-op).</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ChestClosed : PacketBase<ChestClosed>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.ChestClosed;

    /// <summary>Id of the chest.</summary>
    [SerializeOrder(0)] public int ObjectId { get; set; }
}

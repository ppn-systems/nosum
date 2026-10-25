// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Placeable.C2S;

/// <summary>Stops watching a chest. No reply.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CloseChest : PacketBase<CloseChest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.CloseChest;

    /// <summary>Id of the placed chest.</summary>
    [WireRange(0, int.MaxValue)]
    [SerializeOrder(0)] public int ObjectId { get; set; }
}

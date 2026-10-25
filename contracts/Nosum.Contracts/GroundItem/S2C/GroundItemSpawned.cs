// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.GroundItem.S2C;

/// <summary>Pushed to the players of an instance when an item appeared on the ground.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GroundItemSpawned : PacketBase<GroundItemSpawned>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => GroundItemOpCode.GroundItemSpawned;

    /// <summary>The map the item lies on.</summary>
    [SerializeOrder(0)] public string MapId { get; set; } = string.Empty;

    /// <summary>The item.</summary>
    [SerializeOrder(1)] public GroundItemRow Item { get; set; } = new();
}

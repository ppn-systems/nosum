// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.GroundItem.S2C;

/// <summary>Pushed to the players of an instance when an item left the ground.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GroundItemTaken : PacketBase<GroundItemTaken>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => GroundItemOpCode.GroundItemTaken;

    /// <summary>The map the item lay on.</summary>
    [SerializeOrder(0)] public string MapId { get; set; } = string.Empty;

    /// <summary>The ground item id.</summary>
    [SerializeOrder(1)] public long Id { get; set; }

    /// <summary>Why it left.</summary>
    [SerializeOrder(2)] public GroundItemGone Reason { get; set; }

    /// <summary>Where the taker stood, x pixels; the client flies the item there. Zero unless <see cref="Reason"/> is <see cref="GroundItemGone.PickedUp"/>.</summary>
    [SerializeOrder(3)] public ushort ToX { get; set; }

    /// <summary>Where the taker stood, y pixels.</summary>
    [SerializeOrder(4)] public ushort ToY { get; set; }
}

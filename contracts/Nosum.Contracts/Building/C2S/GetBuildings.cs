// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Building.C2S;

/// <summary>Asks for the farm buildings: those of the land the player stands on, or (at the carpenter) those of their own farm and co-op land.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetBuildings : PacketBase<GetBuildings>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => BuildingOpCode.GetBuildings;

    /// <summary><see langword="true"/> for the player's own lands, <see langword="false"/> for the place they stand in.</summary>
    [SerializeOrder(0)] public bool Mine { get; set; }
}

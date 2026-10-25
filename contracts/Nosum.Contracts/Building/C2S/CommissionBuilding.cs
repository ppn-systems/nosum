// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Building.C2S;

/// <summary>Asks the carpenter for the blueprint of a building: the server takes the coins, wood and stone and hands over the blueprint (docs/adr/ADR-028 D1).</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CommissionBuilding : PacketBase<CommissionBuilding>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => BuildingOpCode.CommissionBuilding;

    /// <summary>The building kind (<c>BuildingKind</c> in Nosum.Gameplay).</summary>
    [SerializeOrder(0)] public byte Kind { get; set; }
}

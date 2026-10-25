// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Mine.C2S;

/// <summary>Rides the mine elevator to a stop (docs/adr/ADR-026 D3). The server checks the player stands at an elevator and has been that deep.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RideElevator : PacketBase<RideElevator>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MineOpCode.RideElevator;

    /// <summary>The stop: 0 for the entrance cave, or 5, 10, 15 ... 40. The wire bound is far above the deepest floor; the server checks the stop itself.</summary>
    [WireRange(0, 255)]
    [SerializeOrder(0)] public int Floor { get; set; }
}

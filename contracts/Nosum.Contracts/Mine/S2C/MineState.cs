// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Mine.S2C;

/// <summary>Reply to every mine request: the outcome, the floor the player is on (0 on the entrance cave or outside the mine) and the deepest floor they ever reached.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MineState : PacketBase<MineState>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MineOpCode.MineState;

    /// <summary>Outcome of the request.</summary>
    [SerializeOrder(0)] public MineStatus Status { get; set; }

    /// <summary>The floor the player is on; zero on the entrance cave and everywhere outside the mine.</summary>
    [SerializeOrder(1)] public int Floor { get; set; }

    /// <summary>The deepest floor the player reached; the elevator offers every fifth floor down to it.</summary>
    [SerializeOrder(2)] public int Deepest { get; set; }
}

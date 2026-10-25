// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Goals.S2C;

/// <summary>Reply to <see cref="C2S.DeliverToGoal"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DeliverResult : PacketBase<DeliverResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => GoalOpCode.DeliverResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public GoalStatus Status { get; set; }

    /// <summary>How many items were taken; zero unless the status is Ok.</summary>
    [SerializeOrder(1)] public int Delivered { get; set; }
}

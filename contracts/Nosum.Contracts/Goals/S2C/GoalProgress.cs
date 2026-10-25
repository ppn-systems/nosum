// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Goals.S2C;

/// <summary>Pushed to everyone in the Town when a goal's progress changed, so the board shows the new total without asking.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GoalProgress : PacketBase<GoalProgress>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => GoalOpCode.GoalProgress;

    /// <summary>The goal id.</summary>
    [SerializeOrder(0)] public int GoalId { get; set; }

    /// <summary>The season number the goal row belongs to.</summary>
    [SerializeOrder(1)] public int SeasonNumber { get; set; }

    /// <summary>How many items everybody delivered.</summary>
    [SerializeOrder(2)] public long Total { get; set; }

    /// <summary>Where the goal is in its life.</summary>
    [SerializeOrder(3)] public GoalState State { get; set; }

    /// <summary>How many players delivered anything.</summary>
    [SerializeOrder(4)] public int Contributors { get; set; }
}

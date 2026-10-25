// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Goals.C2S;

/// <summary>Delivers items into a community goal. The server checks the player stands at the Town board, takes the items from the inventory and counts them, all in one transaction.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DeliverToGoal : PacketBase<DeliverToGoal>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => GoalOpCode.DeliverToGoal;

    /// <summary>The goal id (<c>CommunityGoalCatalog</c>).</summary>
    [SerializeOrder(0)] public int GoalId { get; set; }

    /// <summary>How many items. More than the goal still takes delivers only what fits.</summary>
    [SerializeOrder(1)] public int Quantity { get; set; }
}

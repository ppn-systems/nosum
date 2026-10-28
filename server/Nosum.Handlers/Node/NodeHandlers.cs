// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Nodes;
using Nosum.Contracts.Node;
using Nosum.Contracts.Node.C2S;
using Nosum.Contracts.Node.S2C;
using Nosum.Gameplay.Nodes;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Node;

/// <summary>
/// Reading the resource nodes of the place the player stands in (docs/adr/ADR-021). Using a tool on a node is the farm intent
/// (<c>UseItem</c>); the node changes reach the instance as pushes.
/// </summary>
[PacketHandler("Nosum.Node")]
public static partial class NodeHandlers
{
    [Inject] private static IResourceNodeService s_nodes = null!;

    /// <summary>Replies with every node of the place the player stands in.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(NodeOpCode.GetNodes)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "node-read")]
    public static async ValueTask HandleGetNodesAsync(IPacketContext<GetNodes> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        NodeSnapshotView view = await s_nodes.GetAsync(context.Connection, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<GetNodes, NodeSnapshot>(context, sequence, snapshot =>
        {
            snapshot.MapId = view.MapId;
            snapshot.WaterLeft = (byte)view.WaterLeft;
            snapshot.WaterCapacity = (byte)view.WaterCapacity;
            snapshot.Nodes.Clear();
            foreach (ResourceNode node in view.Nodes)
            {
                NodeRow row = NodeRow.Create();
                row.Id = node.Id;
                row.X = (ushort)node.X;
                row.Y = (ushort)node.Y;
                row.Kind = (byte)node.Kind;
                row.Variant = node.Variant;
                row.Stage = node.Stage;
                row.Hits = node.Hits;
                snapshot.Nodes.Add(row);
            }
        }).ConfigureAwait(false);
    }
}

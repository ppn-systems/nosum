// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nalix.Codec.Extensions;
using Nalix.Codec.Pooling;
using Nalix.Runtime.Extensions;
using Nosum.Abstractions.GroundItems;
using Nosum.Contracts.GroundItem;
using Nosum.Contracts.GroundItem.C2S;
using Nosum.Contracts.GroundItem.S2C;

namespace Nosum.Handlers.GroundItem;

/// <summary>
/// Handlers for the items lying on the ground (docs/adr/ADR-021). Picking up has no request: the server picks up when the
/// player's authoritative position moves near an item, so the only request is reading what lies in the player's instance.
/// </summary>
[PacketHandler("Nosum.GroundItem")]
public static partial class GroundItemHandlers
{
    [Inject] private static IGroundItems s_items = null!;

    /// <summary>Replies with the ground items of the instance the player stands in.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(GroundItemOpCode.GetGroundItems)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "ground-items-read")]
    public static async ValueTask HandleGetGroundItemsAsync(IPacketContext<GetGroundItems> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        GroundItemsView view = await s_items.GetAsync(context.Connection, context.CancellationToken).ConfigureAwait(false);

        PacketScope<GroundItemsSnapshot> lease = PacketFactory<GroundItemsSnapshot>.Acquire();
        try
        {
            GroundItemsSnapshot snapshot = lease.Value;
            snapshot.SequenceId = sequence;
            snapshot.MapId = view.MapId;
            snapshot.Items.Clear();
            foreach (GroundItemView item in view.Items)
            {
                GroundItemRow row = GroundItemRow.Create();
                row.Id = item.Id;
                row.ItemId = item.ItemId;
                row.Quantity = item.Quantity;
                row.X = (ushort)item.X;
                row.Y = (ushort)item.Y;
                snapshot.Items.Add(row);
            }

            await context.Sender.ReplyAsync(snapshot).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}

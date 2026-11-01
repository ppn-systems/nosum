// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Inventory;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Inventory;

// The backpack at the tailor (docs/adr/ADR-081): the size and the purchase of the next one. Kept in its own file; the class and its group are InventoryHandlers.
public static partial class InventoryHandlers
{
    [Inject] private static IBackpackService s_backpack = null!;

    /// <summary>Replies with the player's backpack size and the next upgrade.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(InventoryOpCode.GetBackpack)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "backpack-read")]
    public static async ValueTask HandleGetBackpackAsync(IPacketContext<GetBackpack> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        BackpackView view = await s_backpack.GetAsync(context.Connection, context.CancellationToken).ConfigureAwait(false);
        await ReplyBackpackAsync(context, sequence, view).ConfigureAwait(false);
    }

    /// <summary>Buys the next backpack size at the tailor's counter.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(InventoryOpCode.UpgradeBackpack)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(3, 5, PolicyId = "backpack-buy")]
    public static async ValueTask HandleUpgradeBackpackAsync(IPacketContext<UpgradeBackpack> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        BackpackView view = await s_backpack.UpgradeAsync(context.Connection, context.CancellationToken).ConfigureAwait(false);
        await ReplyBackpackAsync(context, sequence, view).ConfigureAwait(false);
    }

    private static ValueTask ReplyBackpackAsync<TRequest>(IPacketContext<TRequest> context, ushort sequence, BackpackView view)
        where TRequest : class, IPacket
        => Replies.ReplyAsync<TRequest, BackpackState>(context, sequence, r =>
        {
            r.Status = view.Status;
            r.Capacity = (byte)view.Capacity;
            r.NextCapacity = (byte)view.NextCapacity;
            r.NextPrice = view.NextPrice;
        });
}

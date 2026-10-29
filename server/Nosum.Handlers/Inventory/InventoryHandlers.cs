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

/// <summary>Reading the inventory and moving or selecting slots. The server owns the items; every change is validated by <see cref="IInventoryService"/>.</summary>
[PacketHandler("Nosum.Inventory")]
public static partial class InventoryHandlers
{
    [Inject] private static IInventoryService s_inventory = null!;

    /// <summary>Replies with the whole inventory.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(InventoryOpCode.GetInventory)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "inventory-read")]
    public static async ValueTask HandleGetInventoryAsync(IPacketContext<GetInventory> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        InventoryView view = await s_inventory.GetAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<GetInventory, InventorySnapshot>(context, sequence, snapshot =>
        {
            snapshot.Capacity = (byte)view.Capacity;
            snapshot.SelectedSlot = (byte)view.SelectedSlot;
            snapshot.Slots.Clear();
            foreach (InventorySlotView slot in view.Slots)
            {
                InventorySlotRow row = InventorySlotRow.Create();
                row.Slot = (byte)slot.Slot;
                row.ItemId = slot.ItemId;
                row.Quantity = slot.Quantity;
                snapshot.Slots.Add(row);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Moves, splits, merges or swaps stacks; the changed slots follow as an inventory update.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(InventoryOpCode.MoveSlot)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(10, 3, PolicyId = "inventory-move")]
    public static async ValueTask HandleMoveSlotAsync(IPacketContext<MoveSlot> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int from = context.Packet.From;
        int to = context.Packet.To;
        int quantity = context.Packet.Quantity;
        PlayerIdentity player = context.Connection.GetPlayer();

        InventoryChange change = await s_inventory.MoveAsync(player.PlayerId, from, to, quantity, context.CancellationToken).ConfigureAwait(false);
        await ReplyResultAsync(context, sequence, change.Status, -1, player).ConfigureAwait(false);
    }

    /// <summary>Sorts the player's inventory; the changed slots follow as an inventory update.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(InventoryOpCode.SortInventory)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(3, 5, PolicyId = "inventory-sort")]
    public static async ValueTask HandleSortInventoryAsync(IPacketContext<SortInventory> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        InventoryChange change = await s_inventory.SortAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await ReplyResultAsync(context, sequence, change.Status, -1, player).ConfigureAwait(false);
    }

    /// <summary>Selects the hotbar slot the player holds.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(InventoryOpCode.SelectSlot)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(10, 3, PolicyId = "inventory-select")]
    public static async ValueTask HandleSelectSlotAsync(IPacketContext<SelectSlot> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int slot = context.Packet.Slot;
        PlayerIdentity player = context.Connection.GetPlayer();

        (InventoryStatus status, int selected) = await s_inventory.SelectSlotAsync(player.PlayerId, slot, context.CancellationToken).ConfigureAwait(false);
        await ReplyResultAsync(context, sequence, status, selected, player).ConfigureAwait(false);
    }

    // A move does not report the held slot; read it back so the reply is always complete.
    private static async ValueTask ReplyResultAsync<TRequest>(IPacketContext<TRequest> context, ushort sequence, InventoryStatus status, int selected, PlayerIdentity player)
        where TRequest : class, IPacket
    {
        if (selected < 0)
        {
            selected = (await s_inventory.GetAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false)).SelectedSlot;
        }

        await Replies.ReplyAsync<TRequest, InventoryResult>(context, sequence, r =>
        {
            r.Status = status;
            r.SelectedSlot = (byte)selected;
        }).ConfigureAwait(false);
    }
}

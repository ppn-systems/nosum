// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Placeables;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Placeable;

/// <summary>Opening a chest and moving stacks between it and the player's inventory. The client says which chest, which slots and how many; the server decides who may open it and what moves (docs/adr/ADR-024 D4).</summary>
[PacketHandler("Nosum.Chest")]
public static partial class ChestHandlers
{
    [Inject] private static IChestService s_chests = null!;

    /// <summary>Opens a chest and replies with its contents.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(PlaceableOpCode.OpenChest)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 4, PolicyId = "chest-open")]
    public static async ValueTask HandleOpenChestAsync(IPacketContext<OpenChest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        ChestOpenOutcome outcome = await s_chests.OpenAsync(context.Connection, context.Packet.ObjectId, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<OpenChest, ChestSnapshot>(context, sequence, snapshot =>
        {
            snapshot.Status = outcome.Status;
            snapshot.ObjectId = outcome.ObjectId;
            snapshot.Capacity = (byte)outcome.Capacity;
            snapshot.Slots.Clear();
            foreach (InventorySlotView slot in outcome.Slots)
            {
                InventorySlotRow row = InventorySlotRow.Create();
                row.Slot = (byte)slot.Slot;
                row.ItemId = slot.ItemId;
                row.Quantity = slot.Quantity;
                snapshot.Slots.Add(row);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Stops watching a chest. No reply.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the player stopped watching.</returns>
    [PacketOpcode(PlaceableOpCode.CloseChest)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 4, PolicyId = "chest-open")]
    public static async ValueTask HandleCloseChestAsync(IPacketContext<CloseChest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await s_chests.CloseAsync(context.Connection, context.Packet.ObjectId, context.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Moves a stack, or part of it; replies with the outcome. The changed slots follow as pushes.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(PlaceableOpCode.ChestMove)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(12, 6, PolicyId = "chest-move")]
    public static async ValueTask HandleChestMoveAsync(IPacketContext<ChestMove> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        ChestMove request = context.Packet;
        int objectId = request.ObjectId;
        int toSlot = request.ToSlot == byte.MaxValue ? -1 : request.ToSlot;
        ChestStatus status = await s_chests.MoveAsync(context.Connection, objectId, request.FromSide, request.FromSlot, request.ToSide, toSlot, request.Quantity, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<ChestMove, ChestResult>(context, sequence, r =>
        {
            r.Status = status;
            r.ObjectId = objectId;
        }).ConfigureAwait(false);
    }

    /// <summary>Sorts an open chest; replies with the outcome. The changed slots follow as pushes to the viewers.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(PlaceableOpCode.ChestSort)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(3, 5, PolicyId = "chest-sort")]
    public static async ValueTask HandleChestSortAsync(IPacketContext<ChestSort> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int objectId = context.Packet.ObjectId;
        ChestStatus status = await s_chests.SortAsync(context.Connection, objectId, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<ChestSort, ChestResult>(context, sequence, r =>
        {
            r.Status = status;
            r.ObjectId = objectId;
        }).ConfigureAwait(false);
    }
}

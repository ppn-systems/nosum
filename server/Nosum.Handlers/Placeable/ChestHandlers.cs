// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nalix.Codec.Extensions;
using Nalix.Codec.Pooling;
using Nalix.Runtime.Extensions;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Placeables;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;

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

        PacketScope<ChestSnapshot> lease = PacketFactory<ChestSnapshot>.Acquire();
        try
        {
            ChestSnapshot snapshot = lease.Value;
            snapshot.SequenceId = sequence;
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

            await context.Sender.ReplyAsync(snapshot).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
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
        int toSlot = request.ToSlot == byte.MaxValue ? -1 : request.ToSlot;
        ChestStatus status = await s_chests.MoveAsync(context.Connection, request.ObjectId, request.FromSide, request.FromSlot, request.ToSide, toSlot, request.Quantity, context.CancellationToken).ConfigureAwait(false);

        PacketScope<ChestResult> lease = PacketFactory<ChestResult>.Acquire();
        try
        {
            lease.Value.SequenceId = sequence;
            lease.Value.Status = status;
            lease.Value.ObjectId = request.ObjectId;
            await context.Sender.ReplyAsync(lease.Value).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}

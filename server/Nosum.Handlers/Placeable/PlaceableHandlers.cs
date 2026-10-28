// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Placeables;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Gameplay.Placeables;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Placeable;

/// <summary>
/// Placing, moving and picking up objects in the player's own place, and reading what is placed. The client says which slot, object and
/// tile; the server decides everything else (docs/adr/ADR-015). Changes reach the instance as a push.
/// </summary>
[PacketHandler("Nosum.Placeable")]
public static partial class PlaceableHandlers
{
    [Inject] private static IPlaceableService s_placeables = null!;

    /// <summary>Replies with the placed objects of the place the player is in.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(Nosum.Contracts.Placeable.PlaceableOpCode.GetPlaced)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "placeable-read")]
    public static async ValueTask HandleGetPlacedAsync(IPacketContext<GetPlaced> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlacedSnapshotView view = await s_placeables.GetAsync(context.Connection, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<GetPlaced, PlacedSnapshot>(context, sequence, snapshot =>
        {
            snapshot.MapId = view.MapId;
            snapshot.Objects.Clear();
            foreach (PlacedItem item in view.Objects)
            {
                PlacedObjectRow row = PlacedObjectRow.Create();
                row.Id = item.Id;
                row.ItemId = item.ItemId;
                row.TileX = (ushort)item.TileX;
                row.TileY = (ushort)item.TileY;
                row.Rotation = (byte)item.Rotation;
                snapshot.Objects.Add(row);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Places the item in an inventory slot.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(Nosum.Contracts.Placeable.PlaceableOpCode.PlaceObject)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(8, 3, PolicyId = "placeable-edit")]
    public static async ValueTask HandlePlaceObjectAsync(IPacketContext<PlaceObject> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlaceOutcome outcome = await s_placeables.PlaceAsync(context.Connection, context.Packet.Slot, context.Packet.TileX, context.Packet.TileY, context.Packet.Rotation, context.CancellationToken).ConfigureAwait(false);
        await ReplyAsync(context, sequence, outcome).ConfigureAwait(false);
    }

    /// <summary>Moves or rotates a placed object.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(Nosum.Contracts.Placeable.PlaceableOpCode.MoveObject)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(8, 3, PolicyId = "placeable-edit")]
    public static async ValueTask HandleMoveObjectAsync(IPacketContext<MoveObject> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlaceOutcome outcome = await s_placeables.MoveAsync(context.Connection, context.Packet.ObjectId, context.Packet.TileX, context.Packet.TileY, context.Packet.Rotation, context.CancellationToken).ConfigureAwait(false);
        await ReplyAsync(context, sequence, outcome).ConfigureAwait(false);
    }

    /// <summary>Picks a placed object up into the inventory.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(Nosum.Contracts.Placeable.PlaceableOpCode.PickUpObject)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(8, 3, PolicyId = "placeable-edit")]
    public static async ValueTask HandlePickUpObjectAsync(IPacketContext<PickUpObject> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlaceOutcome outcome = await s_placeables.PickUpAsync(context.Connection, context.Packet.ObjectId, context.CancellationToken).ConfigureAwait(false);
        await ReplyAsync(context, sequence, outcome).ConfigureAwait(false);
    }

    private static ValueTask ReplyAsync<TRequest>(IPacketContext<TRequest> context, ushort sequence, PlaceOutcome outcome)
        where TRequest : class, IPacket
        => Replies.ReplyAsync<TRequest, PlaceResult>(context, sequence, r =>
        {
            r.Status = outcome.Status;
            r.ObjectId = outcome.ObjectId;
        });
}

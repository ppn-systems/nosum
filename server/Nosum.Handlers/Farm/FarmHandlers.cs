// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Farm;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Farm;

/// <summary>
/// Using an item on a tile and reading the farm. The client says which hotbar slot and which tile; the
/// server decides everything else (docs/adr/ADR-011). The tile change itself reaches the map as a push.
/// </summary>
[PacketHandler("Nosum.Farm")]
public static partial class FarmHandlers
{
    [Inject] private static IFarmService s_farm = null!;

    /// <summary>Uses the item in a hotbar slot on a tile.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(FarmOpCode.UseItem)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(8, 3, PolicyId = "farm-use")]
    public static async ValueTask HandleUseItemAsync(IPacketContext<UseItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int slot = context.Packet.Slot;
        int tileX = context.Packet.TileX;
        int tileY = context.Packet.TileY;

        FarmUseOutcome outcome = await s_farm.UseAsync(context.Connection, slot, tileX, tileY, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<UseItem, UseItemResult>(context, sequence, r =>
        {
            r.Status = outcome.Status;
            r.Action = outcome.Action;
        }).ConfigureAwait(false);
    }

    /// <summary>Uses a hoe or a watering can on a line of tiles with one swing (docs/adr/ADR-026 D1); the server checks the claim against the item in the slot.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(FarmOpCode.UseItemArea)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(8, 3, PolicyId = "farm-use")]
    public static async ValueTask HandleUseItemAreaAsync(IPacketContext<UseItemArea> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int slot = context.Packet.Slot;
        int tileX = context.Packet.TileX;
        int tileY = context.Packet.TileY;
        int tiles = context.Packet.Tiles;

        FarmUseOutcome outcome = await s_farm.UseAreaAsync(context.Connection, slot, tileX, tileY, tiles, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<UseItemArea, UseItemResult>(context, sequence, r =>
        {
            r.Status = outcome.Status;
            r.Action = outcome.Action;
        }).ConfigureAwait(false);
    }

    /// <summary>Replies with the worked tiles of the map the player stands on.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(FarmOpCode.GetFarm)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "farm-read")]
    public static async ValueTask HandleGetFarmAsync(IPacketContext<GetFarm> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        FarmSnapshotView view = await s_farm.GetFarmAsync(context.Connection, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<GetFarm, FarmSnapshot>(context, sequence, snapshot =>
        {
            snapshot.MapId = view.MapId;
            snapshot.ServerNowMs = view.ServerNowMs;
            snapshot.Tiles.Clear();
            foreach (FarmTileView tile in view.Tiles)
            {
                FarmTileRow row = FarmTileRow.Create();
                row.X = (ushort)tile.X;
                row.Y = (ushort)tile.Y;
                row.CropId = tile.State.CropId;
                row.PlantedAtMs = tile.State.PlantedAtMs;
                row.GrowthDays = tile.State.GrowthDays;
                row.WateredDay = tile.State.WateredDay;
                snapshot.Tiles.Add(row);
            }
        }).ConfigureAwait(false);
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nalix.Codec.Extensions;
using Nalix.Codec.Pooling;
using Nalix.Runtime.Extensions;
using Nosum.Abstractions.Farm;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;

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

        PacketScope<UseItemResult> lease = PacketFactory<UseItemResult>.Acquire();
        try
        {
            lease.Value.SequenceId = sequence;
            lease.Value.Status = outcome.Status;
            lease.Value.Action = outcome.Action;
            await context.Sender.ReplyAsync(lease.Value).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
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

        PacketScope<FarmSnapshot> lease = PacketFactory<FarmSnapshot>.Acquire();
        try
        {
            FarmSnapshot snapshot = lease.Value;
            snapshot.SequenceId = sequence;
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
                row.GrowthMs = tile.State.GrowthMs;
                row.SettledAtMs = tile.State.SettledAtMs;
                row.WetUntilMs = tile.State.WetUntilMs;
                snapshot.Tiles.Add(row);
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

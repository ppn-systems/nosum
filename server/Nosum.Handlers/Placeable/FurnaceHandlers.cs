// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nalix.Codec.Extensions;
using Nalix.Codec.Pooling;
using Nalix.Runtime.Extensions;
using Nosum.Abstractions.Placeables;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;

namespace Nosum.Handlers.Placeable;

/// <summary>Starting a furnace job, collecting the bar and reading the furnaces of the place. The client names the furnace and the ore; the server decides the rest (docs/adr/ADR-024 D4).</summary>
[PacketHandler("Nosum.Furnace")]
public static partial class FurnaceHandlers
{
    [Inject] private static IFurnaceService s_furnaces = null!;

    /// <summary>Starts a job; replies with the outcome.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(PlaceableOpCode.StartSmelting)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 4, PolicyId = "furnace")]
    public static async ValueTask HandleStartSmeltingAsync(IPacketContext<StartSmelting> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        FurnaceOutcome outcome = await s_furnaces.StartAsync(context.Connection, context.Packet.ObjectId, context.Packet.OreItemId, context.CancellationToken).ConfigureAwait(false);
        await ReplyAsync(context, sequence, outcome).ConfigureAwait(false);
    }

    /// <summary>Collects a finished bar; replies with the outcome.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(PlaceableOpCode.CollectFurnace)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 4, PolicyId = "furnace")]
    public static async ValueTask HandleCollectFurnaceAsync(IPacketContext<CollectFurnace> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        FurnaceOutcome outcome = await s_furnaces.CollectAsync(context.Connection, context.Packet.ObjectId, context.CancellationToken).ConfigureAwait(false);
        await ReplyAsync(context, sequence, outcome).ConfigureAwait(false);
    }

    /// <summary>Replies with the furnaces of the place that have a job.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(PlaceableOpCode.GetFurnaces)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "furnace-read")]
    public static async ValueTask HandleGetFurnacesAsync(IPacketContext<GetFurnaces> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        IReadOnlyList<FurnaceView> views = await s_furnaces.GetAsync(context.Connection, context.CancellationToken).ConfigureAwait(false);

        PacketScope<FurnaceSnapshot> lease = PacketFactory<FurnaceSnapshot>.Acquire();
        try
        {
            lease.Value.SequenceId = sequence;
            lease.Value.Furnaces.Clear();
            foreach (FurnaceView view in views)
            {
                FurnaceRow row = FurnaceRow.Create();
                row.ObjectId = view.ObjectId;
                row.State = view.State;
                row.OreItemId = view.OreItemId;
                row.RemainingMs = view.RemainingMs;
                lease.Value.Furnaces.Add(row);
            }

            await context.Sender.ReplyAsync(lease.Value).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static async ValueTask ReplyAsync<TRequest>(IPacketContext<TRequest> context, ushort sequence, FurnaceOutcome outcome)
        where TRequest : IPacket
    {
        PacketScope<FurnaceResult> lease = PacketFactory<FurnaceResult>.Acquire();
        try
        {
            lease.Value.SequenceId = sequence;
            lease.Value.Status = outcome.Status;
            lease.Value.ObjectId = outcome.ObjectId;
            lease.Value.ItemId = outcome.ItemId;
            lease.Value.Quantity = outcome.Quantity;
            await context.Sender.ReplyAsync(lease.Value).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}

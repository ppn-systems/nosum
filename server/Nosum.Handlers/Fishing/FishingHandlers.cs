// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Fishing;
using Nosum.Contracts.Fishing;
using Nosum.Contracts.Fishing.C2S;
using Nosum.Contracts.Fishing.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Fishing;

/// <summary>
/// Casting a fishing rod and reeling the line in (docs/adr/ADR-071). The packets carry the intent only: the tile of the cast, and the cast id
/// on the reel-in. The server times both presses on its own clock and rolls the catch.
/// </summary>
[PacketHandler("Nosum.Fishing")]
public static partial class FishingHandlers
{
    [Inject] private static IFishingService s_fishing = null!;

    /// <summary>Casts the held rod at a water tile and replies with the bite delay and the press window.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(FishingOpCode.CastRod)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(20, 10, PolicyId = "fishing-cast")]
    public static async ValueTask HandleCastRodAsync(IPacketContext<CastRod> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int tileX = context.Packet.TileX;
        int tileY = context.Packet.TileY;
        CastOutcome outcome = await s_fishing.CastAsync(context.Connection, tileX, tileY, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<CastRod, CastResult>(context, sequence, reply =>
        {
            reply.Status = outcome.Status;
            reply.CastId = outcome.CastId;
            reply.BiteDelayMs = outcome.BiteDelayMs;
            reply.WindowMs = outcome.WindowMs;
        }).ConfigureAwait(false);
    }

    /// <summary>Reels the line in and replies with what came up.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(FishingOpCode.ReelIn)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(30, 10, PolicyId = "fishing-reel")]
    public static async ValueTask HandleReelInAsync(IPacketContext<ReelIn> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        uint castId = context.Packet.CastId;
        ReelOutcome outcome = await s_fishing.ReelInAsync(context.Connection, castId, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<ReelIn, ReelResult>(context, sequence, reply =>
        {
            reply.Status = outcome.Status;
            reply.ItemId = outcome.ItemId;
            reply.Quantity = outcome.Quantity;
            reply.XpGained = outcome.XpGained;
        }).ConfigureAwait(false);
    }
}

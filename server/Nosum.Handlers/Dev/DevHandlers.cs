// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Calendar;
using Nosum.Abstractions.Shops;
using Nosum.Contracts.Dev;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Dev;

/// <summary>
/// Tester tools. The host maps this class ONLY when it runs in the Development environment (<c>Program.BuildApplication</c>); in Production the
/// opcode is not registered, so a frame for it is dropped and <see cref="IDevGrantService"/> does not exist.
/// </summary>
[PacketHandler("Nosum.Dev")]
public static partial class DevHandlers
{
    [Inject] private static IDevGrantService s_grants = null!;
    [Inject] private static IDevClockService s_clock = null!;

    /// <summary>Gives the sender items and/or coins.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(DevOpCode.DevGrant)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(8, 4, PolicyId = "dev-grant")]
    public static async ValueTask HandleDevGrantAsync(IPacketContext<DevGrant> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int itemId = context.Packet.ItemId;
        int quantity = context.Packet.Quantity;
        long coins = context.Packet.Coins;
        PlayerIdentity player = context.Connection.GetPlayer();

        bool ok = await s_grants.GrantAsync(player.PlayerId, itemId, quantity, coins, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<DevGrant, DevGrantResult>(context, sequence, r => r.Ok = ok).ConfigureAwait(false);
    }

    /// <summary>Moves the server's game clock forward by whole game days (crop growth and day rollovers can then be tested at once).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(DevOpCode.DevAdvanceClock)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(8, 4, PolicyId = "dev-clock")]
    public static async ValueTask HandleDevAdvanceClockAsync(IPacketContext<DevAdvanceClock> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        long? now = s_clock.AdvanceDays(context.Packet.Days);
        await Replies.ReplyAsync<DevAdvanceClock, DevAdvanceClockResult>(context, sequence, r =>
        {
            r.Ok = now is not null;
            r.ServerNowMs = now ?? 0;
        }).ConfigureAwait(false);
    }
}

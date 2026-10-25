// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Coop;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Coop.C2S;
using Nosum.Contracts.Coop.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Coop;

/// <summary>Co-ops: membership and the shared warehouse (docs/adr/ADR-014). Identity comes from the connection; roles and withdrawal rights are the service's.</summary>
[PacketHandler("Nosum.Coop")]
public static partial class CoopHandlers
{
    [Inject] private static ICoopService s_coops = null!;

    /// <summary>Replies with the player's co-op (or their invitations).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(CoopOpCode.GetCoop)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "coop-read")]
    public static async ValueTask HandleGetCoopAsync(IPacketContext<GetCoop> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        CoopView view = await s_coops.GetAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<GetCoop, CoopSnapshot>(context, sequence, view.FillSnapshot).ConfigureAwait(false);
    }

    /// <summary>Founds a co-op.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(CoopOpCode.CreateCoop)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "coop-write")]
    public static async ValueTask HandleCreateCoopAsync(IPacketContext<CreateCoop> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        string name = context.Packet.Name;
        PlayerIdentity player = context.Connection.GetPlayer();

        CoopStatus status = await s_coops.CreateAsync(player.PlayerId, name, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<CreateCoop, CoopResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Invites a player on the same map.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(CoopOpCode.InvitePlayer)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "coop-write")]
    public static async ValueTask HandleInvitePlayerAsync(IPacketContext<InvitePlayer> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        uint target = context.Packet.TargetPlayerId;
        PlayerIdentity player = context.Connection.GetPlayer();

        CoopStatus status = await s_coops.InviteAsync(context.Connection, player.PlayerId, target, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<InvitePlayer, CoopResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Accepts or declines an invitation.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(CoopOpCode.RespondInvite)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "coop-write")]
    public static async ValueTask HandleRespondInviteAsync(IPacketContext<RespondInvite> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        Guid inviteId = context.Packet.InviteId;
        bool accept = context.Packet.Accept;
        PlayerIdentity player = context.Connection.GetPlayer();

        CoopStatus status = await s_coops.RespondAsync(player.PlayerId, inviteId, accept, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<RespondInvite, CoopResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Leaves the co-op.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(CoopOpCode.LeaveCoop)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "coop-write")]
    public static async ValueTask HandleLeaveCoopAsync(IPacketContext<LeaveCoop> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        CoopStatus status = await s_coops.LeaveAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<LeaveCoop, CoopResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Removes a member (founder only).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(CoopOpCode.KickMember)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "coop-write")]
    public static async ValueTask HandleKickMemberAsync(IPacketContext<KickMember> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        Guid memberId = context.Packet.MemberId;
        PlayerIdentity player = context.Connection.GetPlayer();

        CoopStatus status = await s_coops.KickAsync(player.PlayerId, memberId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<KickMember, CoopResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Moves items from the inventory into the warehouse.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(CoopOpCode.DepositItem)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(10, 4, PolicyId = "coop-stock")]
    public static async ValueTask HandleDepositItemAsync(IPacketContext<DepositItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int itemId = context.Packet.ItemId;
        int quantity = context.Packet.Quantity;
        PlayerIdentity player = context.Connection.GetPlayer();

        CoopStatus status = await s_coops.DepositAsync(player.PlayerId, itemId, quantity, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<DepositItem, CoopResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Moves items from the warehouse into the inventory.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(CoopOpCode.WithdrawItem)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(10, 4, PolicyId = "coop-stock")]
    public static async ValueTask HandleWithdrawItemAsync(IPacketContext<WithdrawItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int itemId = context.Packet.ItemId;
        int quantity = context.Packet.Quantity;
        PlayerIdentity player = context.Connection.GetPlayer();

        CoopStatus status = await s_coops.WithdrawAsync(player.PlayerId, itemId, quantity, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<WithdrawItem, CoopResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Buys the next level of a co-op upgrade, paid from the warehouse stock (founder only).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(CoopOpCode.UpgradeCoop)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(10, 4, PolicyId = "coop-stock")]
    public static async ValueTask HandleUpgradeCoopAsync(IPacketContext<UpgradeCoop> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        CoopUpgradeTrack track = context.Packet.Track;
        int targetLevel = context.Packet.TargetLevel;
        PlayerIdentity player = context.Connection.GetPlayer();

        CoopStatus status = await s_coops.UpgradeAsync(player.PlayerId, track, targetLevel, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<UpgradeCoop, CoopResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }
}

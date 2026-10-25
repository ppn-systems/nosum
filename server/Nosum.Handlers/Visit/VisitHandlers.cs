// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Visits;
using Nosum.Contracts.Visit;
using Nosum.Contracts.Visit.C2S;
using Nosum.Contracts.Visit.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Visit;

/// <summary>
/// Visiting another player's farm and farmhouse (docs/adr/ADR-019). The owner edits their own allow-list (the identity is the connection's, so nobody can edit another's);
/// a visit request names a nickname and the service checks the allow-list every time. Nothing here trusts a client-supplied place.
/// </summary>
[PacketHandler("Nosum.Visit")]
public static partial class VisitHandlers
{
    [Inject] private static IVisitService s_visits = null!;

    /// <summary>Replies with the player's allow-list and who is in their place.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(VisitOpCode.GetVisitors)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "visit-read")]
    public static async ValueTask HandleGetVisitorsAsync(IPacketContext<GetVisitors> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        VisitorsView view = await s_visits.GetAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<GetVisitors, VisitorsSnapshot>(context, sequence, view.FillSnapshot).ConfigureAwait(false);
    }

    /// <summary>Adds a player to the allow-list.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(VisitOpCode.AddVisitor)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "visit-write")]
    public static async ValueTask HandleAddVisitorAsync(IPacketContext<AddVisitor> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        string nickname = context.Packet.Nickname;
        PlayerIdentity player = context.Connection.GetPlayer();

        VisitStatus status = await s_visits.AddAsync(player.PlayerId, nickname, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<AddVisitor, VisitResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Removes a player from the allow-list and sends them out.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(VisitOpCode.RemoveVisitor)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "visit-write")]
    public static async ValueTask HandleRemoveVisitorAsync(IPacketContext<RemoveVisitor> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        Guid visitor = context.Packet.PlayerId;
        PlayerIdentity player = context.Connection.GetPlayer();

        VisitStatus status = await s_visits.RemoveAsync(player.PlayerId, visitor, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<RemoveVisitor, VisitResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Sends a player out of the owner's place for now.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(VisitOpCode.KickVisitor)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "visit-write")]
    public static async ValueTask HandleKickVisitorAsync(IPacketContext<KickVisitor> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        Guid visitor = context.Packet.PlayerId;
        PlayerIdentity player = context.Connection.GetPlayer();

        VisitStatus status = await s_visits.KickAsync(player.PlayerId, visitor, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<KickVisitor, VisitResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Turns "co-op members may visit" on or off.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(VisitOpCode.SetCoopVisits)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "visit-write")]
    public static async ValueTask HandleSetCoopVisitsAsync(IPacketContext<SetCoopVisits> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        bool allowed = context.Packet.Allowed;
        PlayerIdentity player = context.Connection.GetPlayer();

        VisitStatus status = await s_visits.SetCoopMembersMayVisitAsync(player.PlayerId, allowed, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<SetCoopVisits, VisitResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Asks to visit another player's farm.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(VisitOpCode.VisitPlayer)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(3, 2, PolicyId = "visit-go")]
    public static async ValueTask HandleVisitPlayerAsync(IPacketContext<VisitPlayer> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        string nickname = context.Packet.Nickname;
        PlayerIdentity player = context.Connection.GetPlayer();

        VisitStatus status = await s_visits.VisitAsync(context.Connection, player.PlayerId, nickname, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<VisitPlayer, VisitResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }
}

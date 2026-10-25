// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Admin;
using Nosum.Contracts.Admin;
using Nosum.Contracts.Admin.C2S;
using Nosum.Contracts.Admin.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Admin;

/// <summary>
/// The admin intents (docs/adr/ADR-031 D7). Two locks: the fail-closed permission middleware admits only a connection that signed in as an
/// admin account, and <see cref="IAdminService"/> re-reads the role from the database on every call, so taking the role away works at once.
/// Every success is audited by the service. The admin role itself can only be granted from the host's command line, never by a packet here.
/// Encrypted on the wire: the report reads carry chat text.
/// </summary>
[PacketHandler("Nosum.Admin")]
public static partial class AdminHandlers
{
    [Inject] private static IAdminService s_admin = null!;

    /// <summary>Ends a player's connection (admin only).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(AdminOpCode.Kick)]
    [PacketPermission(PermissionLevel.SYSTEM_ADMINISTRATOR)]
    [PacketEncryption(true)]
    [PacketRateLimit(5, 10, PolicyId = "admin")]
    public static async ValueTask HandleKickAsync(IPacketContext<AdminKick> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        string nickname = context.Packet.Nickname;
        Guid admin = context.Connection.GetPlayer().PlayerId;

        AdminStatus status = await s_admin.KickAsync(admin, nickname, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<AdminKick, AdminResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Mutes a player's chat (admin only).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(AdminOpCode.Mute)]
    [PacketPermission(PermissionLevel.SYSTEM_ADMINISTRATOR)]
    [PacketEncryption(true)]
    [PacketRateLimit(5, 10, PolicyId = "admin")]
    public static async ValueTask HandleMuteAsync(IPacketContext<AdminMute> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        string nickname = context.Packet.Nickname;
        int hours = context.Packet.Hours;
        Guid admin = context.Connection.GetPlayer().PlayerId;

        AdminStatus status = await s_admin.MuteAsync(admin, nickname, hours, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<AdminMute, AdminResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Bans a player (admin only).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(AdminOpCode.Ban)]
    [PacketPermission(PermissionLevel.SYSTEM_ADMINISTRATOR)]
    [PacketEncryption(true)]
    [PacketRateLimit(5, 10, PolicyId = "admin")]
    public static async ValueTask HandleBanAsync(IPacketContext<AdminBan> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        string nickname = context.Packet.Nickname;
        Guid admin = context.Connection.GetPlayer().PlayerId;

        AdminStatus status = await s_admin.BanAsync(admin, nickname, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<AdminBan, AdminResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Lifts a ban (admin only).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(AdminOpCode.Unban)]
    [PacketPermission(PermissionLevel.SYSTEM_ADMINISTRATOR)]
    [PacketEncryption(true)]
    [PacketRateLimit(5, 10, PolicyId = "admin")]
    public static async ValueTask HandleUnbanAsync(IPacketContext<AdminUnban> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        string nickname = context.Packet.Nickname;
        Guid admin = context.Connection.GetPlayer().PlayerId;

        AdminStatus status = await s_admin.UnbanAsync(admin, nickname, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<AdminUnban, AdminResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Gives a player items or coins (admin only).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(AdminOpCode.GrantItem)]
    [PacketPermission(PermissionLevel.SYSTEM_ADMINISTRATOR)]
    [PacketEncryption(true)]
    [PacketRateLimit(5, 10, PolicyId = "admin")]
    public static async ValueTask HandleGrantItemAsync(IPacketContext<AdminGrantItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        string nickname = context.Packet.Nickname;
        int itemId = context.Packet.ItemId;
        int quantity = context.Packet.Quantity;
        long coins = context.Packet.Coins;
        Guid admin = context.Connection.GetPlayer().PlayerId;

        AdminStatus status = await s_admin.GrantAsync(admin, nickname, itemId, quantity, coins, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<AdminGrantItem, AdminResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Lists the newest chat reports (admin only).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(AdminOpCode.ListReports)]
    [PacketPermission(PermissionLevel.SYSTEM_ADMINISTRATOR)]
    [PacketEncryption(true)]
    [PacketRateLimit(2, 4, PolicyId = "admin-read")]
    public static async ValueTask HandleListReportsAsync(IPacketContext<AdminListReports> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        Guid admin = context.Connection.GetPlayer().PlayerId;

        AdminReadResult<AdminReportSummary> result = await s_admin.ListReportsAsync(admin, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<AdminListReports, AdminReportList>(context, sequence, r =>
        {
            r.Status = result.Status;
            r.Reports.Clear();
            foreach (AdminReportSummary row in result.Items)
            {
                AdminReportRow item = AdminReportRow.Create();
                item.ReportId = row.Id;
                item.Reporter = row.Reporter;
                item.Reported = row.Reported;
                item.CreatedAtMs = row.CreatedAt.ToUnixTimeMilliseconds();
                r.Reports.Add(item);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Reads the stored context of one chat report (admin only).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(AdminOpCode.ReadReport)]
    [PacketPermission(PermissionLevel.SYSTEM_ADMINISTRATOR)]
    [PacketEncryption(true)]
    [PacketRateLimit(4, 8, PolicyId = "admin-read")]
    public static async ValueTask HandleReadReportAsync(IPacketContext<AdminReadReport> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        Guid reportId = context.Packet.ReportId;
        Guid admin = context.Connection.GetPlayer().PlayerId;

        AdminReadResult<AdminReportLine> result = await s_admin.ReadReportAsync(admin, reportId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<AdminReadReport, AdminReportDetail>(context, sequence, r =>
        {
            r.Status = result.Status;
            r.Lines.Clear();
            foreach (AdminReportLine line in result.Items)
            {
                AdminReportLineRow item = AdminReportLineRow.Create();
                item.Channel = line.Channel;
                item.Sender = line.Sender;
                item.Text = line.Text;
                item.SentAtMs = line.SentAt.ToUnixTimeMilliseconds();
                r.Lines.Add(item);
            }
        }).ConfigureAwait(false);
    }
}

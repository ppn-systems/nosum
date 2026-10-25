// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Admin;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Observability;
using Nosum.Contracts.Account;
using Nosum.Contracts.Admin;
using Nosum.Domain.Accounts;
using Nosum.Domain.Admin;
using Nosum.Domain.Social;
using Nosum.Gameplay.Items;
using Nosum.Infrastructure.Chat;

namespace Nosum.Infrastructure.Admin;

/// <summary>
/// <see cref="IAdminService"/> (docs/adr/ADR-031 D7). Each call reads the caller's role from the database first, so a handler's permission
/// level is only the first lock; refuses to act on oneself or on another admin; changes the database before the live state (mute list,
/// connection), so a crash never leaves a ban that only exists in memory; and writes one <see cref="AdminAuditEntry"/> in the same
/// transaction as the change it records.
/// </summary>
public sealed class AdminService : IAdminService
{
    private const long MaxGrantCoins = 1_000_000_000;

    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ISessionRegistry _sessions;
    private readonly MuteList _mutes;
    private readonly ITransferService _transfers;
    private readonly TimeProvider _time;

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="sessions">Ends a player's connection.</param>
    /// <param name="mutes">The live mute list the database column is mirrored into.</param>
    /// <param name="transfers">Moves granted items and coins.</param>
    /// <param name="time">The clock stamping audit rows and mutes.</param>
    public AdminService(IUnitOfWorkFactory unitOfWork, ISessionRegistry sessions, MuteList mutes, ITransferService transfers, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(mutes);
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(time);

        _unitOfWork = unitOfWork;
        _sessions = sessions;
        _mutes = mutes;
        _transfers = transfers;
        _time = time;
    }

    /// <inheritdoc/>
    public async Task<AdminStatus> KickAsync(Guid adminId, string nickname, CancellationToken cancellationToken = default)
    {
        (AdminStatus status, Guid targetId) = await this.ApplyAsync(adminId, nickname, AdminAction.Kick, string.Empty, static _ => { }, cancellationToken).ConfigureAwait(false);
        if (status != AdminStatus.Ok)
        {
            return status;
        }

        // The audit row says a kick was ordered; the reply says whether anyone was there to receive it.
        return await _sessions.EndAsync(targetId, AccountStatus.SessionKicked, cancellationToken).ConfigureAwait(false) ? AdminStatus.Ok : AdminStatus.NotOnline;
    }

    /// <inheritdoc/>
    public async Task<AdminStatus> MuteAsync(Guid adminId, string nickname, int hours, CancellationToken cancellationToken = default)
    {
        if (hours < 0 || hours > AdminLimits.MaxMuteHours)
        {
            return await this.RefuseAsync(adminId, AdminStatus.InvalidDuration, cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset until = hours == 0 ? default : _time.GetUtcNow().AddHours(hours);
        (AdminStatus status, Guid targetId) = await this.ApplyAsync(
            adminId,
            nickname,
            AdminAction.Mute,
            "hours=" + hours.ToString(CultureInfo.InvariantCulture),
            player => player.MutedUntil = hours == 0 ? null : until,
            cancellationToken).ConfigureAwait(false);
        if (status == AdminStatus.Ok)
        {
            _mutes.Set(targetId, until);
        }

        return status;
    }

    /// <inheritdoc/>
    public async Task<AdminStatus> BanAsync(Guid adminId, string nickname, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        (AdminStatus status, Guid targetId) = await this.ApplyAsync(adminId, nickname, AdminAction.Ban, string.Empty, player => player.BannedAt ??= now, cancellationToken).ConfigureAwait(false);
        if (status == AdminStatus.Ok)
        {
            _ = await _sessions.EndAsync(targetId, AccountStatus.Banned, cancellationToken).ConfigureAwait(false);
        }

        return status;
    }

    /// <inheritdoc/>
    public async Task<AdminStatus> UnbanAsync(Guid adminId, string nickname, CancellationToken cancellationToken = default)
        => (await this.ApplyAsync(adminId, nickname, AdminAction.Unban, string.Empty, static player => player.BannedAt = null, cancellationToken).ConfigureAwait(false)).Status;

    /// <inheritdoc/>
    public async Task<AdminStatus> GrantAsync(Guid adminId, string nickname, int itemId, int quantity, long coins, CancellationToken cancellationToken = default)
    {
        int items = 0;
        if (itemId != 0)
        {
            if (!ItemCatalog.TryGet(itemId, out ItemDefinition? item))
            {
                return await this.RefuseAsync(adminId, AdminStatus.InvalidItem, cancellationToken).ConfigureAwait(false);
            }

            if (quantity < 1 || quantity > item.MaxStack)
            {
                return await this.RefuseAsync(adminId, AdminStatus.InvalidQuantity, cancellationToken).ConfigureAwait(false);
            }

            items = quantity;
        }
        else if (coins == 0)
        {
            return await this.RefuseAsync(adminId, AdminStatus.InvalidItem, cancellationToken).ConfigureAwait(false);
        }

        if (coins < 0 || coins > MaxGrantCoins)
        {
            return await this.RefuseAsync(adminId, AdminStatus.InvalidQuantity, cancellationToken).ConfigureAwait(false);
        }

        // A grant to oneself or another admin is allowed: it is how a game master tests and compensates.
        string detail = string.Create(CultureInfo.InvariantCulture, $"item={itemId} qty={items} coins={coins}");
        (AdminStatus status, Guid targetId) = await this.ApplyAsync(adminId, nickname, AdminAction.Grant, detail, static _ => { }, cancellationToken, allowSelf: true).ConfigureAwait(false);
        if (status != AdminStatus.Ok)
        {
            return status;
        }

        TransferStatus moved = await _transfers.ExecuteAsync(
            [new TransferLeg(TransferParty.Player(targetId), itemId, items, coins)],
            "admin.grant",
            null,
            cancellationToken).ConfigureAwait(false);
        if (moved == TransferStatus.Ok)
        {
            return AdminStatus.Ok;
        }

        // The audit row records the order that was given; the log says it did not go through.
        OpsLog.Warning("admin.grant_failed", "An admin grant was refused by the transfer service.", new LogField("admin_id", adminId), new LogField("target_id", targetId), new LogField("transfer", moved.ToString()));
        return AdminStatus.GrantFailed;
    }

    /// <inheritdoc/>
    public async Task<AdminReadResult<AdminReportSummary>> ListReportsAsync(Guid adminId, CancellationToken cancellationToken = default)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            if (!await IsAdminAsync(unit, adminId, cancellationToken).ConfigureAwait(false))
            {
                return new AdminReadResult<AdminReportSummary>(AdminStatus.NotAdmin, []);
            }

            List<ChatReport> reports = await unit.Repository<ChatReport>()
                .PageAsync(static _ => true, static r => r.CreatedAt, 0, AdminLimits.ReportPage, cancellationToken).ConfigureAwait(false);
            List<AdminReportSummary> rows = new(reports.Count);
            foreach (ChatReport report in reports)
            {
                string reporter = (await unit.Repository<Player>().FindAsync(report.ReporterId, cancellationToken).ConfigureAwait(false))?.Nickname ?? string.Empty;
                string reported = (await unit.Repository<Player>().FindAsync(report.ReportedId, cancellationToken).ConfigureAwait(false))?.Nickname ?? string.Empty;
                rows.Add(new AdminReportSummary(report.Id, reporter, reported, report.CreatedAt));
            }

            return new AdminReadResult<AdminReportSummary>(AdminStatus.Ok, rows);
        }
    }

    /// <inheritdoc/>
    public async Task<AdminReadResult<AdminReportLine>> ReadReportAsync(Guid adminId, Guid reportId, CancellationToken cancellationToken = default)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            if (!await IsAdminAsync(unit, adminId, cancellationToken).ConfigureAwait(false))
            {
                return new AdminReadResult<AdminReportLine>(AdminStatus.NotAdmin, []);
            }

            if (!await unit.Repository<ChatReport>().AnyAsync(r => r.Id == reportId, cancellationToken).ConfigureAwait(false))
            {
                return new AdminReadResult<AdminReportLine>(AdminStatus.UnknownReport, []);
            }

            List<ChatReportLine> lines = await unit.Repository<ChatReportLine>().ListAsync(l => l.ReportId == reportId, cancellationToken).ConfigureAwait(false);
            lines.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));
            List<AdminReportLine> rows = new(lines.Count);
            foreach (ChatReportLine line in lines)
            {
                rows.Add(new AdminReportLine(line.Channel, line.SenderNickname, line.Text, line.SentAt));
            }

            return new AdminReadResult<AdminReportLine>(AdminStatus.Ok, rows);
        }
    }

    private static async Task<bool> IsAdminAsync(IUnitOfWork unit, Guid adminId, CancellationToken cancellationToken)
    {
        Player? admin = await unit.Repository<Player>().FindAsync(adminId, cancellationToken).ConfigureAwait(false);
        return admin is { Role: PlayerRole.Admin };
    }

    // Logs a refusal that happened before any player was resolved.
    private async Task<AdminStatus> RefuseAsync(Guid adminId, AdminStatus status, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            if (!await IsAdminAsync(unit, adminId, cancellationToken).ConfigureAwait(false))
            {
                status = AdminStatus.NotAdmin;
            }
        }

        NosumMetrics.IntentRejected("admin", status.ToString());
        return status;
    }

    // The shared path of the nickname-targeted intents: check the caller, resolve the target, refuse the forbidden ones, change the row and audit it
    // in one transaction.
    private async Task<(AdminStatus Status, Guid TargetId)> ApplyAsync(
        Guid adminId,
        string nickname,
        AdminAction action,
        string detail,
        Action<Player> change,
        CancellationToken cancellationToken,
        bool allowSelf = false)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            if (!await IsAdminAsync(unit, adminId, cancellationToken).ConfigureAwait(false))
            {
                return Refused(AdminStatus.NotAdmin);
            }

            if (!BlockList.TryKey(nickname, out string key))
            {
                return Refused(AdminStatus.InvalidNickname);
            }

            Player? target = await unit.Repository<Player>().FirstOrDefaultAsync(p => p.NormalizedNickname == key, cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                return Refused(AdminStatus.UnknownPlayer);
            }

            if (target.Id == adminId && !allowSelf)
            {
                return Refused(AdminStatus.SelfTarget);
            }

            if (target.Role == PlayerRole.Admin && target.Id != adminId && !allowSelf)
            {
                return Refused(AdminStatus.TargetIsAdmin);
            }

            change(target);
            unit.Repository<AdminAuditEntry>().Add(new AdminAuditEntry
            {
                AdminId = adminId,
                Action = (byte)action,
                TargetId = target.Id,
                Detail = detail,
                CreatedAt = _time.GetUtcNow(),
            });
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            OpsLog.Information(
                "admin." + action.ToString().ToLowerInvariant(),
                "An admin acted on a player.",
                new LogField("admin_id", adminId),
                new LogField("target_id", target.Id),
                new LogField("detail", detail));
            return (AdminStatus.Ok, target.Id);
        }
    }

    private static (AdminStatus Status, Guid TargetId) Refused(AdminStatus status)
    {
        NosumMetrics.IntentRejected("admin", status.ToString());
        return (status, Guid.Empty);
    }
}

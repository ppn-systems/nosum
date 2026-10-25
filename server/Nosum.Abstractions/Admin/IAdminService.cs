// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Admin;

namespace Nosum.Abstractions.Admin;

/// <summary>
/// The admin intents (docs/adr/ADR-031 D7). Every method re-reads the caller's role from the database, so revoking the role
/// takes effect at once, and writes one audit row on success. Handlers call this; they never touch the database.
/// </summary>
public interface IAdminService
{
    /// <summary>Ends a player's connection; the player may sign in again.</summary>
    /// <param name="adminId">The signed-in admin, taken from the connection identity.</param>
    /// <param name="nickname">The target's nickname.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok, or the refusal.</returns>
    Task<AdminStatus> KickAsync(Guid adminId, string nickname, CancellationToken cancellationToken = default);

    /// <summary>Stops a player from chatting for some hours; zero hours lifts a mute.</summary>
    /// <param name="adminId">The signed-in admin, taken from the connection identity.</param>
    /// <param name="nickname">The target's nickname.</param>
    /// <param name="hours">How long, 0 to <see cref="AdminLimits.MaxMuteHours"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok, or the refusal.</returns>
    Task<AdminStatus> MuteAsync(Guid adminId, string nickname, int hours, CancellationToken cancellationToken = default);

    /// <summary>Bans a player: their connection ends and they cannot sign in or resume.</summary>
    /// <param name="adminId">The signed-in admin, taken from the connection identity.</param>
    /// <param name="nickname">The target's nickname.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok, or the refusal.</returns>
    Task<AdminStatus> BanAsync(Guid adminId, string nickname, CancellationToken cancellationToken = default);

    /// <summary>Lifts a ban.</summary>
    /// <param name="adminId">The signed-in admin, taken from the connection identity.</param>
    /// <param name="nickname">The target's nickname.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok, or the refusal.</returns>
    Task<AdminStatus> UnbanAsync(Guid adminId, string nickname, CancellationToken cancellationToken = default);

    /// <summary>Gives a player items and/or coins through the transfer service.</summary>
    /// <param name="adminId">The signed-in admin, taken from the connection identity.</param>
    /// <param name="nickname">The receiver's nickname.</param>
    /// <param name="itemId">The item, or zero for coins only.</param>
    /// <param name="quantity">How many items, at most one stack.</param>
    /// <param name="coins">How many coins, zero for none.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok, or the refusal.</returns>
    Task<AdminStatus> GrantAsync(Guid adminId, string nickname, int itemId, int quantity, long coins, CancellationToken cancellationToken = default);

    /// <summary>Lists the newest chat reports (docs/adr/ADR-029 D3), newest first.</summary>
    /// <param name="adminId">The signed-in admin, taken from the connection identity.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok with the reports, or the refusal with none.</returns>
    Task<AdminReadResult<AdminReportSummary>> ListReportsAsync(Guid adminId, CancellationToken cancellationToken = default);

    /// <summary>Reads the stored context of one chat report, oldest line first.</summary>
    /// <param name="adminId">The signed-in admin, taken from the connection identity.</param>
    /// <param name="reportId">The report.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok with the lines, or the refusal with none.</returns>
    Task<AdminReadResult<AdminReportLine>> ReadReportAsync(Guid adminId, Guid reportId, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of an admin read.</summary>
/// <typeparam name="T">The row type.</typeparam>
/// <param name="Status">Ok, or the refusal.</param>
/// <param name="Items">The rows; empty unless <paramref name="Status"/> is Ok.</param>
public readonly record struct AdminReadResult<T>(AdminStatus Status, IReadOnlyList<T> Items);

/// <summary>One chat report in a list.</summary>
/// <param name="Id">The report id.</param>
/// <param name="Reporter">Nickname of the player who reported.</param>
/// <param name="Reported">Nickname of the player who was reported.</param>
/// <param name="CreatedAt">When the report was made.</param>
public readonly record struct AdminReportSummary(Guid Id, string Reporter, string Reported, DateTimeOffset CreatedAt);

/// <summary>One stored message of a chat report.</summary>
/// <param name="Channel">The channel (a <c>ChatChannel</c> value).</param>
/// <param name="Sender">The sender's nickname when the message was sent.</param>
/// <param name="Text">The text as delivered.</param>
/// <param name="SentAt">When it was sent.</param>
public readonly record struct AdminReportLine(byte Channel, string Sender, string Text, DateTimeOffset SentAt);

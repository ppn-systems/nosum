// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Mail;
using Nosum.Contracts.Mail.S2C;

namespace Nosum.Abstractions.Mail;

/// <summary>An item line attached to a mail.</summary>
/// <param name="ItemId">The item id.</param>
/// <param name="Quantity">How many.</param>
public readonly record struct MailItem(int ItemId, int Quantity);

/// <summary>Items and coins a mail carries; collected by one atomic transfer.</summary>
/// <param name="Items">The item lines (at most <c>MailRules.MaxItemLines</c>).</param>
/// <param name="Coins">The coins; 0 for none.</param>
public sealed record MailAttachments(IReadOnlyList<MailItem> Items, long Coins = 0)
{
    /// <summary>Gets whether nothing is attached.</summary>
    public bool IsEmpty => this.Items.Count == 0 && this.Coins == 0;

    /// <summary>Creates an attachment of coins only.</summary>
    /// <param name="coins">The coins.</param>
    /// <returns>The attachment.</returns>
    public static MailAttachments OfCoins(long coins) => new([], coins);
}

/// <summary>One mail as listed to its recipient.</summary>
/// <param name="Id">The mail id.</param>
/// <param name="Code">The message code.</param>
/// <param name="Args">The arguments of the code.</param>
/// <param name="SentAt">When it was sent.</param>
/// <param name="ExpiresAt">When it expires.</param>
/// <param name="Read">Whether the player read it.</param>
/// <param name="Coins">Coins attached.</param>
/// <param name="Items">Items attached.</param>
/// <param name="Collected">Whether the attachment was collected.</param>
public sealed record MailView(Guid Id, string Code, IReadOnlyList<string> Args, DateTimeOffset SentAt, DateTimeOffset ExpiresAt, bool Read, long Coins, IReadOnlyList<MailItem> Items, bool Collected)
{
    /// <summary>Copies the view into a row of the mail list packet.</summary>
    /// <returns>The row.</returns>
    public MailRow ToRow()
    {
        MailRow row = MailRow.Create();
        row.Id = this.Id;
        row.Code = this.Code;
        foreach (string arg in this.Args)
        {
            MailArgRow a = MailArgRow.Create();
            a.Value = arg;
            row.Args.Add(a);
        }

        row.SentAtMs = this.SentAt.ToUnixTimeMilliseconds();
        row.ExpiresAtMs = this.ExpiresAt.ToUnixTimeMilliseconds();
        row.Read = this.Read;
        row.Coins = this.Coins;
        foreach (MailItem item in this.Items)
        {
            MailItemRow i = MailItemRow.Create();
            i.ItemId = item.ItemId;
            i.Quantity = item.Quantity;
            row.Items.Add(i);
        }

        row.Collected = this.Collected;
        return row;
    }
}

/// <summary>
/// A mailbox per player for system messages (docs/adr/ADR-029 D5): sold listings, co-op invitations, community goal rewards, shop deliveries.
/// A mail is a code plus arguments (the client renders it from its resources), may carry items and coins collected by one atomic transfer,
/// and expires after 30 days. Other features send mail through <see cref="SendAsync"/>.
/// </summary>
public interface IMailService
{
    /// <summary>Puts a mail into a player's mailbox and tells them if they are online. Never throws for a normal refusal; a full mailbox still accepts system mail.</summary>
    /// <param name="recipientId">The recipient's account id.</param>
    /// <param name="code">The message code (see <c>MailRules.Codes</c>).</param>
    /// <param name="args">The arguments of the code; clipped to the limits in <c>MailRules</c>.</param>
    /// <param name="attachments">Items and coins to attach; <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The id of the new mail.</returns>
    Task<Guid> SendAsync(Guid recipientId, string code, IReadOnlyList<string> args, MailAttachments? attachments = null, CancellationToken cancellationToken = default);

    /// <summary>Lists the player's mails that have not expired, newest first.</summary>
    /// <param name="playerId">The recipient.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The mails.</returns>
    Task<IReadOnlyList<MailView>> ListAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Counts the player's unread mails that have not expired.</summary>
    /// <param name="playerId">The recipient.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The count.</returns>
    Task<int> CountUnreadAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Marks a mail read.</summary>
    /// <param name="playerId">The recipient.</param>
    /// <param name="mailId">The mail.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<MailStatus> ReadAsync(Guid playerId, Guid mailId, CancellationToken cancellationToken = default);

    /// <summary>Moves the attachment into the player's inventory and wallet in one atomic transfer; a second collect of the same mail is refused.</summary>
    /// <param name="playerId">The recipient.</param>
    /// <param name="mailId">The mail.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<MailStatus> CollectAsync(Guid playerId, Guid mailId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a mail that carries no uncollected attachment.</summary>
    /// <param name="playerId">The recipient.</param>
    /// <param name="mailId">The mail.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<MailStatus> DeleteAsync(Guid playerId, Guid mailId, CancellationToken cancellationToken = default);

    /// <summary>Removes every expired mail, attachment and all.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many mails were removed.</returns>
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);
}

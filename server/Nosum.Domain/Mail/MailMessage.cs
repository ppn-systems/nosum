// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

namespace Nosum.Domain.Mail;

/// <summary>
/// One mail in a player's mailbox (docs/adr/ADR-029 D5): a system message as a code plus arguments, optionally carrying items and coins.
/// <see cref="CollectedAt"/> is a concurrency token: the transfer that hands over the attachment sets it inside its own transaction, so
/// two collects of one mail cannot both succeed.
/// </summary>
public sealed class MailMessage : Entity
{
    /// <summary>Gets or sets the player whose mailbox holds the mail.</summary>
    public Guid RecipientId { get; set; }

    /// <summary>Gets or sets the message code, for example <c>market.sold</c>.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets or sets the arguments of the code.</summary>
    public string[] Args { get; set; } = [];

    /// <summary>Gets or sets when the mail was sent.</summary>
    public DateTimeOffset SentAt { get; set; }

    /// <summary>Gets or sets when the mail expires; an expired mail is invisible and removed with its attachment.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Gets or sets when the player read it; <see langword="null"/> while unread.</summary>
    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>Gets or sets the coins attached; 0 for none.</summary>
    public long Coins { get; set; }

    /// <summary>Gets or sets when the attachment was collected; <see langword="null"/> while it waits (or when there is none).</summary>
    public DateTimeOffset? CollectedAt { get; set; }
}

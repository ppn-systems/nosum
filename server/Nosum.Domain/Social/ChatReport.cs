// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

namespace Nosum.Domain.Social;

/// <summary>A report of a player's chat, kept for the admins (docs/adr/ADR-029 D3). The only chat text the server persists.</summary>
public sealed class ChatReport : Entity
{
    /// <summary>Gets or sets the player who reported.</summary>
    public Guid ReporterId { get; set; }

    /// <summary>Gets or sets the player who was reported.</summary>
    public Guid ReportedId { get; set; }

    /// <summary>Gets or sets when the report was made.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One message of the context stored with a <see cref="ChatReport"/>: what the reporter had received, oldest first.</summary>
public sealed class ChatReportLine
{
    /// <summary>Gets or sets the report.</summary>
    public Guid ReportId { get; set; }

    /// <summary>Gets or sets the position in the context, 0 for the oldest.</summary>
    public int Sequence { get; set; }

    /// <summary>Gets or sets the channel (a <c>ChatChannel</c> value).</summary>
    public byte Channel { get; set; }

    /// <summary>Gets or sets the sender's account id.</summary>
    public Guid SenderId { get; set; }

    /// <summary>Gets or sets the sender's nickname when the message was sent.</summary>
    public string SenderNickname { get; set; } = string.Empty;

    /// <summary>Gets or sets the text as delivered (cleaned and masked).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets when the message was sent.</summary>
    public DateTimeOffset SentAt { get; set; }
}

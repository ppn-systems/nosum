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

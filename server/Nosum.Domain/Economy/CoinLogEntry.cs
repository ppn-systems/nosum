// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

namespace Nosum.Domain.Economy;

/// <summary>One change to a player's coins: the audit trail, written in the same transaction as the change.</summary>
public sealed class CoinLogEntry : Entity
{
    /// <summary>Gets or sets the player whose balance changed.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the change: positive when coins were added, negative when taken.</summary>
    public long Delta { get; set; }

    /// <summary>Gets or sets why, a stable code such as <c>market.buy</c> (never display text).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Gets or sets when the change happened.</summary>
    public DateTimeOffset OccurredAt { get; set; }
}

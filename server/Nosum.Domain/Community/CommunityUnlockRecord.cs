// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Community;

/// <summary>A permanent unlock flag set by a completed goal (docs/adr/ADR-030 D1), asked by other features through <c>ICommunityUnlocks</c>.</summary>
public sealed class CommunityUnlockRecord
{
    /// <summary>Gets or sets the unlock key (<c>CommunityUnlockKeys</c>); also this row's primary key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the goal that set the flag.</summary>
    public int GoalId { get; set; }

    /// <summary>Gets or sets when the flag was set.</summary>
    public DateTimeOffset UnlockedAt { get; set; }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Coop;
using Nosum.Domain.Common;

namespace Nosum.Domain.Coops;

/// <summary>
/// A player's membership of a co-op. A player belongs to at most one co-op (unique player id while they are a member). A member who leaves keeps the row with
/// <see cref="LeftAt"/> set, so a rejoin takes up the same membership id and with it the warehouse contributions the audit log attributes to that id.
/// </summary>
public sealed class CoopMember : Entity
{
    /// <summary>Gets or sets the co-op.</summary>
    public Guid CoopId { get; set; }

    /// <summary>Gets or sets the member's account.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the member's role.</summary>
    public CoopRole Role { get; set; }

    /// <summary>Gets or sets when the player joined.</summary>
    public DateTimeOffset JoinedAt { get; set; }

    /// <summary>Gets or sets when the player left or was removed; <see langword="null"/> while they are a member.</summary>
    public DateTimeOffset? LeftAt { get; set; }
}

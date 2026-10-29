// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Coop;
using Nosum.Domain.Common;

namespace Nosum.Domain.Coops;

/// <summary>A player's membership of a co-op. A player belongs to at most one co-op (unique player id).</summary>
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
}

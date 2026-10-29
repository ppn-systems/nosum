// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

namespace Nosum.Domain.Coops;

/// <summary>An open invitation of a player to a co-op.</summary>
public sealed class CoopInvite : Entity
{
    /// <summary>Gets or sets the co-op that invites.</summary>
    public Guid CoopId { get; set; }

    /// <summary>Gets or sets the invited player.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets when the invitation was made.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the invitation expires.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

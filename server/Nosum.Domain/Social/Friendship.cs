// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Social;

/// <summary>One side of a friendship (docs/adr/ADR-029 D4). A friendship is two rows, one per player, written and removed together.</summary>
public sealed class Friendship
{
    /// <summary>Gets or sets the player whose list the row belongs to.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the friend.</summary>
    public Guid FriendId { get; set; }

    /// <summary>Gets or sets when the two became friends.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An open friend request.</summary>
public sealed class FriendRequest
{
    /// <summary>Gets or sets the player who asked.</summary>
    public Guid FromId { get; set; }

    /// <summary>Gets or sets the player who was asked.</summary>
    public Guid ToId { get; set; }

    /// <summary>Gets or sets when the request was sent.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

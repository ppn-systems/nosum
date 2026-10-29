// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Social;

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

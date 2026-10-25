// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

namespace Nosum.Domain.Accounts;

/// <summary>A session token issued at register or login, kept as a hash so a database leak cannot sign anyone in.</summary>
public sealed class PlayerSessionToken : Entity
{
    /// <summary>Gets or sets the player the token signs in.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the SHA-256 hash (base64) of the token. Unique. The token itself is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Gets or sets when the token was issued.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the token stops being accepted.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Gets or sets when the token was revoked; null while it is valid.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}

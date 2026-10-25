// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

namespace Nosum.Domain.Accounts;

/// <summary>
/// A one-time recovery code sent by e-mail (docs/adr/ADR-032 D4). Kept only as a salted hash: an 8-digit code is
/// short, so a fast hash would not survive a database leak. Single use, expiring and limited to a few attempts.
/// </summary>
public sealed class PasswordResetCode : Entity
{
    /// <summary>Gets or sets the account the code resets.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the self-describing salted hash of the code (<c>IPasswordHasher</c> format). Never the code.</summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>Gets or sets when the code was created (it also counts the account's requests per hour).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the code stops being accepted.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Gets or sets how many wrong codes were tried against this one.</summary>
    public int Attempts { get; set; }

    /// <summary>Gets or sets when the code was used or replaced by a newer one; null while it can still be used.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Common;

namespace Nosum.Domain.Accounts;

/// <summary>A player account.</summary>
public sealed class Player : Entity
{
    /// <summary>Gets or sets the username as the player typed it at registration (display form).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Gets or sets the lower-case invariant form of <see cref="Username"/>; unique, used for lookups.</summary>
    public string NormalizedUsername { get; set; } = string.Empty;

    /// <summary>Gets or sets the self-describing password hash (algorithm, iterations, salt, hash). Never the password.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the preferred UI language as a BCP-47 tag.</summary>
    public string PreferredLanguage { get; set; } = "vi";

    /// <summary>Gets or sets when the account was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the player last signed in with a password or token; null until the first sign-in.</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Gets or sets the state needed to put the player back where they were.</summary>
    public PlayerState? State { get; set; }
}

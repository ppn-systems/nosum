// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Domain.Admin;

/// <summary>What an <see cref="AdminAuditEntry"/> records. Stored as a number; values are append-only.</summary>
public enum AdminAction : byte
{
    /// <summary>A connection was ended.</summary>
    Kick = 1,

    /// <summary>A chat mute was set or lifted.</summary>
    Mute = 2,

    /// <summary>An account was banned.</summary>
    Ban = 3,

    /// <summary>A ban was lifted.</summary>
    Unban = 4,

    /// <summary>Items or coins were granted.</summary>
    Grant = 5,

    /// <summary>The admin role was given from the host's command line.</summary>
    GrantRole = 6,

    /// <summary>The admin role was taken away from the host's command line.</summary>
    RevokeRole = 7,
}

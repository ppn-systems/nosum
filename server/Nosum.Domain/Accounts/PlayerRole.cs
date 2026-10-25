// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Domain.Accounts;

/// <summary>What a player account may do beyond playing. Stored as a number; values are append-only.</summary>
public enum PlayerRole : byte
{
    /// <summary>An ordinary player.</summary>
    Player = 0,

    /// <summary>A game master who may use the admin intents (docs/adr/ADR-031 D7). Granted only by the host's <c>admin</c> command, never over the wire.</summary>
    Admin = 1,
}

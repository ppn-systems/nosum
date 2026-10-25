// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Visit;

/// <summary>Outcome code of a Visit request. Clients map it to text. Values are append-only.</summary>
public enum VisitStatus : byte
{
    /// <summary>Applied.</summary>
    Ok = 0,

    /// <summary>The nickname is not a valid nickname.</summary>
    InvalidNickname = 1,

    /// <summary>No player has that nickname.</summary>
    UnknownPlayer = 2,

    /// <summary>The player named themselves.</summary>
    Self = 3,

    /// <summary>A guest cannot keep an allow-list; register first (docs/adr/ADR-019 D2).</summary>
    GuestOwner = 4,

    /// <summary>The player is on the allow-list already.</summary>
    AlreadyListed = 5,

    /// <summary>The player is not on the allow-list (or no longer in the place).</summary>
    NotListed = 6,

    /// <summary>The allow-list holds as many players as it may.</summary>
    ListFull = 7,

    /// <summary>The owner has not let this player visit.</summary>
    NotAllowed = 8,

    /// <summary>A visit starts from a public map (the Town, the bus stop ...), not from a private place or in the middle of a map change.</summary>
    NotOnPublicMap = 9,

    /// <summary>The player is not in the world yet.</summary>
    NotJoined = 10,

    /// <summary>Another change happened at the same time; ask again.</summary>
    Conflict = 11,
}

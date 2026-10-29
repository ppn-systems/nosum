// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Combat;

/// <summary>Outcome code of a bestiary title request. Clients map it to text. Values are append-only (docs/adr/ADR-067).</summary>
public enum BestiaryStatus : byte
{
    /// <summary>Applied.</summary>
    Ok = 0,

    /// <summary>The species is not in the catalogue.</summary>
    UnknownSpecies = 1,

    /// <summary>The player has not reached the last milestone for that species yet.</summary>
    NotEarned = 2,
}

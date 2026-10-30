// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Pet;

/// <summary>Outcome code of a pet request. Values are append-only.</summary>
public enum PetStatus : byte
{
    /// <summary>Applied.</summary>
    Ok = 0,

    /// <summary>The player has no pet yet.</summary>
    NoPet = 1,

    /// <summary>The player already has a pet; one at a time.</summary>
    AlreadyHasPet = 2,

    /// <summary>The kind is neither a cat nor a dog.</summary>
    InvalidKind = 3,

    /// <summary>The name is empty after cleaning, or too long to keep.</summary>
    InvalidName = 4,

    /// <summary>The pet was already petted today.</summary>
    AlreadyPetted = 5,

    /// <summary>The player is on no map, or the request could not be served right now.</summary>
    Unavailable = 6,
}

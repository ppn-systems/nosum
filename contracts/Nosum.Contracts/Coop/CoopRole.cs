// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Coop;

/// <summary>A member's role in a co-op. Values are append-only.</summary>
public enum CoopRole : byte
{
    /// <summary>Not a member.</summary>
    None = 0,

    /// <summary>Founded the co-op: may invite, kick and take anything from the warehouse.</summary>
    Founder = 1,

    /// <summary>May deposit and take back what they put in.</summary>
    Member = 2,
}

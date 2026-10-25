// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Coop;

/// <summary>Which co-op upgrade is meant (docs/adr/ADR-038). Values are append-only.</summary>
public enum CoopUpgradeTrack : byte
{
    /// <summary>The warehouse holds more item kinds.</summary>
    Warehouse = 1,

    /// <summary>More of the co-op land may be tilled and built on.</summary>
    Land = 2,
}

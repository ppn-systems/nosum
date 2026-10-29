// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Domain.Common;

namespace Nosum.Domain.Coops;

/// <summary>A co-operative: a named group of players with a shared warehouse (docs/adr/ADR-014).</summary>
public sealed class Coop : Entity
{
    /// <summary>Gets or sets the name as the founder typed it: plain text or an <c>i18n{</c> envelope (docs/adr/ADR-010 D5).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets when the co-op was founded.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the warehouse upgrade level, 0 to <c>CoopUpgrades.MaxLevel</c>: how many item kinds the warehouse holds (docs/adr/ADR-038).</summary>
    public int WarehouseLevel { get; set; }

    /// <summary>Gets or sets the land upgrade level, 0 to <c>CoopUpgrades.MaxLevel</c>: how much of the co-op land may be tilled and built on (docs/adr/ADR-038).</summary>
    public int LandLevel { get; set; }

    /// <summary>Gets or sets the shared objective ledger by objective id (docs/adr/ADR-067 D2). Stored as one JSON column; change it by replacing the dictionary.</summary>
    public Dictionary<int, CoopObjectiveProgress> Objectives { get; set; } = [];
}

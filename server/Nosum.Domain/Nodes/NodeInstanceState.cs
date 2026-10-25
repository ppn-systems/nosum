// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Nodes;

/// <summary>
/// Marks that the resource nodes of a map instance were seeded from the map file, so an instance whose nodes were all cleared is
/// never seeded again, and remembers the last game day its nodes were brought up to (docs/adr/ADR-021 D4). One row per instance.
/// </summary>
public sealed class NodeInstanceState
{
    /// <summary>Gets or sets the row id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the map key.</summary>
    public string MapId { get; set; } = string.Empty;

    /// <summary>Gets or sets the owner of the instance; <see cref="Guid.Empty"/> for a shared map.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Gets or sets when the instance was seeded.</summary>
    public DateTimeOffset SeededAt { get; set; }

    /// <summary>Gets or sets the last game day the instance's nodes were brought up to.</summary>
    public long LastDay { get; set; }
}

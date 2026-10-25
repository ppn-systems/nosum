// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Building.S2C;

/// <summary>One animal of a building: what the card shows and where it is going (docs/adr/ADR-028 D3, D4).</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AnimalRow
{
    /// <summary>Creates an empty row.</summary>
    /// <returns>The row.</returns>
    public static AnimalRow Create() => new();

    /// <summary>The animal's id.</summary>
    [SerializeOrder(0)] public Guid Id { get; set; }

    /// <summary>The species.</summary>
    [SerializeOrder(1)] public byte Kind { get; set; }

    /// <summary>The name its owner gave it.</summary>
    [SerializeOrder(2)] public string Name { get; set; } = string.Empty;

    /// <summary>Friendship, 0 to 1000.</summary>
    [SerializeOrder(3)] public int Friendship { get; set; }

    /// <summary>Bit 0: petted today; bit 1: has produce to collect.</summary>
    [SerializeOrder(4)] public byte Flags { get; set; }

    /// <summary>Position now: feet, in interior pixels.</summary>
    [SerializeOrder(5)] public float X { get; set; }

    /// <summary>Position now: feet, in interior pixels.</summary>
    [SerializeOrder(6)] public float Y { get; set; }

    /// <summary>Where the straight walk it is on ends.</summary>
    [SerializeOrder(7)] public float TargetX { get; set; }

    /// <summary>Where the straight walk it is on ends.</summary>
    [SerializeOrder(8)] public float TargetY { get; set; }

    /// <summary>Milliseconds the walk still takes; zero when standing.</summary>
    [SerializeOrder(9)] public int MoveMs { get; set; }
}

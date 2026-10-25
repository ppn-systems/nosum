// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Building.S2C;

/// <summary>One new walk of an animal: a straight line, chosen by the server.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AnimalMoveRow
{
    /// <summary>Creates an empty row.</summary>
    /// <returns>The row.</returns>
    public static AnimalMoveRow Create() => new();

    /// <summary>The animal.</summary>
    [SerializeOrder(0)] public Guid Id { get; set; }

    /// <summary>Start x, interior pixels.</summary>
    [SerializeOrder(1)] public float FromX { get; set; }

    /// <summary>Start y, interior pixels.</summary>
    [SerializeOrder(2)] public float FromY { get; set; }

    /// <summary>End x, interior pixels.</summary>
    [SerializeOrder(3)] public float ToX { get; set; }

    /// <summary>End y, interior pixels.</summary>
    [SerializeOrder(4)] public float ToY { get; set; }

    /// <summary>Milliseconds the walk takes.</summary>
    [SerializeOrder(5)] public int DurationMs { get; set; }
}

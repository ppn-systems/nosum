// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Combat.S2C;

/// <summary>One species and how many of it the player has slain. A plain serializable row: no opcode, no header (docs/adr/ADR-068).</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class BestiaryRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static BestiaryRow Create() => new();

    /// <summary>The monster species as its <c>MonsterKind</c> value.</summary>
    [SerializeOrder(0)] public byte Species { get; set; }

    /// <summary>How many monsters of the species the player has slain.</summary>
    [SerializeOrder(1)] public int Kills { get; set; }
}

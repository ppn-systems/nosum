// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Coop.S2C;

/// <summary>One line of the warehouse audit trail. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CoopLogRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static CoopLogRow Create() => new();

    /// <summary>Who; empty when that member left.</summary>
    [SerializeOrder(0)] public Guid MemberId { get; set; }

    /// <summary>Item id.</summary>
    [SerializeOrder(1)] public int ItemId { get; set; }

    /// <summary>Positive for a deposit, negative for a withdrawal.</summary>
    [SerializeOrder(2)] public int Delta { get; set; }

    /// <summary>When, Unix milliseconds on the server clock.</summary>
    [SerializeOrder(3)] public long AtMs { get; set; }
}

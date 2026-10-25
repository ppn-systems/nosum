// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Coop.S2C;

/// <summary>One member of a co-op. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CoopMemberRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static CoopMemberRow Create() => new();

    /// <summary>The member's id inside the co-op (not the account id).</summary>
    [SerializeOrder(0)] public Guid MemberId { get; set; }

    /// <summary>The member's role.</summary>
    [SerializeOrder(1)] public CoopRole Role { get; set; }

    /// <summary>Whether this is the receiver.</summary>
    [SerializeOrder(2)] public bool IsMe { get; set; }

    /// <summary>Whether the member is connected.</summary>
    [SerializeOrder(3)] public bool Online { get; set; }

    /// <summary>The member's nickname. Appended in protocol version 3.</summary>
    [SerializeOrder(4)] public string Nickname { get; set; } = string.Empty;
}

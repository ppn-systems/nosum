// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Coop.S2C;

/// <summary>An open invitation to the receiver. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CoopInviteRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static CoopInviteRow Create() => new();

    /// <summary>The invitation id.</summary>
    [SerializeOrder(0)] public Guid InviteId { get; set; }

    /// <summary>The co-op's name as authored (plain or i18n envelope).</summary>
    [SerializeOrder(1)] public string CoopName { get; set; } = string.Empty;

    /// <summary>The nickname of the founder who invited; empty when unknown. Appended in protocol version 3.</summary>
    [SerializeOrder(2)] public string InviterNickname { get; set; } = string.Empty;
}

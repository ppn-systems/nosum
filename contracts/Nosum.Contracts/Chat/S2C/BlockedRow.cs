// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Chat.S2C;

/// <summary>One blocked player. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class BlockedRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static BlockedRow Create() => new();

    /// <summary>The blocked player's account id.</summary>
    [SerializeOrder(0)] public Guid PlayerId { get; set; }

    /// <summary>The blocked player's nickname.</summary>
    [SerializeOrder(1)] public string Nickname { get; set; } = string.Empty;
}

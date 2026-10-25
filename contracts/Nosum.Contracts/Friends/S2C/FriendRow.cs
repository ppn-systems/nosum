// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Friends.S2C;

/// <summary>One mutual friend. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class FriendRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static FriendRow Create() => new();

    /// <summary>The friend's account id.</summary>
    [SerializeOrder(0)] public Guid PlayerId { get; set; }

    /// <summary>The friend's nickname.</summary>
    [SerializeOrder(1)] public string Nickname { get; set; } = string.Empty;

    /// <summary>Whether the friend is online.</summary>
    [SerializeOrder(2)] public bool Online { get; set; }

    /// <summary>The public map the friend is on; empty when offline or in a private place.</summary>
    [SerializeOrder(3)] public string MapId { get; set; } = string.Empty;
}

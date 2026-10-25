// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Friends.S2C;

/// <summary>One open friend request. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class FriendRequestRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static FriendRequestRow Create() => new();

    /// <summary>The other player's account id.</summary>
    [SerializeOrder(0)] public Guid PlayerId { get; set; }

    /// <summary>The other player's nickname.</summary>
    [SerializeOrder(1)] public string Nickname { get; set; } = string.Empty;
}

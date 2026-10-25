// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Visit.S2C;

/// <summary>One player the owner lets visit, or who is in the owner's place now. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class VisitorRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static VisitorRow Create() => new();

    /// <summary>The player's account id.</summary>
    [SerializeOrder(0)] public Guid PlayerId { get; set; }

    /// <summary>The player's nickname as stored (a guest has the neutral <c>guest-1234</c> form).</summary>
    [SerializeOrder(1)] public string Nickname { get; set; } = string.Empty;

    /// <summary>Whether the player is on the allow-list. A row that is not listed is a co-op member who is visiting.</summary>
    [SerializeOrder(2)] public bool Listed { get; set; }

    /// <summary>Whether the player is in the owner's farm or farmhouse right now.</summary>
    [SerializeOrder(3)] public bool Present { get; set; }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Market.S2C;

/// <summary>One listing of a page. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ListingRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static ListingRow Create() => new();

    /// <summary>The listing id.</summary>
    [SerializeOrder(0)] public Guid ListingId { get; set; }

    /// <summary>The item.</summary>
    [SerializeOrder(1)] public int ItemId { get; set; }

    /// <summary>How many.</summary>
    [SerializeOrder(2)] public int Quantity { get; set; }

    /// <summary>Price for the whole stack, coins.</summary>
    [SerializeOrder(3)] public long Price { get; set; }

    /// <summary>Expiry, Unix milliseconds on the server clock.</summary>
    [SerializeOrder(4)] public long ExpiresAtMs { get; set; }

    /// <summary>Whether the viewer is the seller.</summary>
    [SerializeOrder(5)] public bool IsMine { get; set; }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Trade.S2C;

/// <summary>One item line of a trade offer. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TradeItemRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static TradeItemRow Create() => new();

    /// <summary>Item id.</summary>
    [WireRange(0, WireLimits.MaxId)]
    [SerializeOrder(0)] public int ItemId { get; set; }

    /// <summary>How many.</summary>
    [WireRange(0, WireLimits.MaxQuantity)]
    [SerializeOrder(1)] public int Quantity { get; set; }
}

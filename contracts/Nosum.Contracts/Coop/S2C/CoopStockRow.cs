// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Coop.S2C;

/// <summary>One item kind in the warehouse. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CoopStockRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static CoopStockRow Create() => new();

    /// <summary>Item id.</summary>
    [SerializeOrder(0)] public int ItemId { get; set; }

    /// <summary>How many the warehouse holds.</summary>
    [SerializeOrder(1)] public int Quantity { get; set; }

    /// <summary>How many the receiver may take out now.</summary>
    [SerializeOrder(2)] public int Available { get; set; }
}

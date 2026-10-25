// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Requests.S2C;

/// <summary>One request of a snapshot. A plain serializable row: no opcode, no header. Names and texts are the client's resx; the row carries ids and numbers.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RequestRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static RequestRow Create() => new();

    /// <summary>The request's slot in its day.</summary>
    [SerializeOrder(0)] public int Slot { get; set; }

    /// <summary>The item to deliver.</summary>
    [SerializeOrder(1)] public int ItemId { get; set; }

    /// <summary>How many items.</summary>
    [SerializeOrder(2)] public int Quantity { get; set; }

    /// <summary>The coins the delivery pays.</summary>
    [SerializeOrder(3)] public long RewardCoins { get; set; }

    /// <summary>Whether the viewer already filled this request today.</summary>
    [SerializeOrder(4)] public bool Filled { get; set; }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Placeable.S2C;

/// <summary>One furnace with a job inside a <see cref="FurnaceSnapshot"/>. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class FurnaceRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static FurnaceRow Create() => new();

    /// <summary>Id of the furnace.</summary>
    [SerializeOrder(0)] public int ObjectId { get; set; }

    /// <summary>Smelting or ready.</summary>
    [SerializeOrder(1)] public FurnaceStateCode State { get; set; }

    /// <summary>The ore being smelted.</summary>
    [SerializeOrder(2)] public int OreItemId { get; set; }

    /// <summary>Milliseconds until the bar is ready, counted from when the packet was sent; zero when ready.</summary>
    [SerializeOrder(3)] public int RemainingMs { get; set; }
}

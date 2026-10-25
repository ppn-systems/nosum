// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Inventory.C2S;

/// <summary>
/// Moves a stack, or part of it, from one slot to another: into an empty slot it moves, onto the same
/// item it merges, onto another item the whole stacks swap.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MoveSlot : PacketBase<MoveSlot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => InventoryOpCode.MoveSlot;

    /// <summary>Source slot.</summary>
    [SerializeOrder(0)] public byte From { get; set; }

    /// <summary>Target slot.</summary>
    [SerializeOrder(1)] public byte To { get; set; }

    /// <summary>How many to move; zero means the whole stack.</summary>
    [WireRange(0, WireLimits.MaxQuantity)]
    [SerializeOrder(2)] public int Quantity { get; set; }
}

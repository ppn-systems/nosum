// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Inventory.S2C;

/// <summary>Reply to <see cref="C2S.GetBackpack"/> and <see cref="C2S.UpgradeBackpack"/>: the outcome, the backpack's size now and the next size with its price.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class BackpackState : PacketBase<BackpackState>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => InventoryOpCode.BackpackState;

    /// <summary>Outcome of the request.</summary>
    [SerializeOrder(0)] public BackpackStatus Status { get; set; }

    /// <summary>Slots the backpack has now.</summary>
    [SerializeOrder(1)] public byte Capacity { get; set; }

    /// <summary>Slots the next size has; zero when the backpack is already the largest.</summary>
    [SerializeOrder(2)] public byte NextCapacity { get; set; }

    /// <summary>Coins the next size costs; zero when there is none.</summary>
    [SerializeOrder(3)] public int NextPrice { get; set; }
}

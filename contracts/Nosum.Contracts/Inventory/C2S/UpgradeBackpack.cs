// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Inventory.C2S;

/// <summary>Buys the next backpack size at the tailor's counter. Reply: <see cref="S2C.BackpackState"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class UpgradeBackpack : PacketBase<UpgradeBackpack>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => InventoryOpCode.UpgradeBackpack;

    /// <summary>Reserved; the request carries no data.</summary>
    [SerializeOrder(0)] public byte Reserved { get; set; }
}

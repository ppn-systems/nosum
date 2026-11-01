// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Inventory.C2S;

/// <summary>Asks for the player's backpack size and the next upgrade. Reply: <see cref="S2C.BackpackState"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetBackpack : PacketBase<GetBackpack>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => InventoryOpCode.GetBackpack;

    /// <summary>Reserved; the request carries no data.</summary>
    [SerializeOrder(0)] public byte Reserved { get; set; }
}

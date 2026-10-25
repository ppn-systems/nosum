// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.S2C;

/// <summary>Pushed to every viewer of a chest after its slots changed: each changed slot in its new state.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ChestUpdate : PacketBase<ChestUpdate>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.ChestUpdate;

    /// <summary>Id of the chest.</summary>
    [SerializeOrder(0)] public int ObjectId { get; set; }

    /// <summary>The changed slots; an empty slot has item id zero.</summary>
    [SerializeOrder(1)] public List<Nosum.Contracts.Inventory.S2C.InventorySlotRow> Slots { get; set; } = [];
}

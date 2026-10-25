// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Inventory.S2C;

/// <summary>Reply to <see cref="C2S.MoveSlot"/> and <see cref="C2S.SelectSlot"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class InventoryResult : PacketBase<InventoryResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => InventoryOpCode.InventoryResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public InventoryStatus Status { get; set; }

    /// <summary>The hotbar slot the player holds after the request.</summary>
    [SerializeOrder(1)] public byte SelectedSlot { get; set; }
}

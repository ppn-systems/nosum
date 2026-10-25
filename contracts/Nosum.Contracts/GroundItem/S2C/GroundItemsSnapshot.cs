// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.GroundItem.S2C;

/// <summary>Reply to <see cref="C2S.GetGroundItems"/>: every item lying in the player's instance.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GroundItemsSnapshot : PacketBase<GroundItemsSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => GroundItemOpCode.GroundItemsSnapshot;

    /// <summary>The map the items lie on; empty when the player is on no map.</summary>
    [SerializeOrder(0)] public string MapId { get; set; } = string.Empty;

    /// <summary>The items.</summary>
    [SerializeOrder(1)] public List<GroundItemRow> Items { get; set; } = [];
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.S2C;

/// <summary>Reply to <see cref="C2S.GetFurnaces"/>: every furnace of the instance that is smelting or holds a bar.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class FurnaceSnapshot : PacketBase<FurnaceSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.FurnaceSnapshot;

    /// <summary>The furnaces with a job; an idle furnace has no row.</summary>
    [SerializeOrder(0)] public List<FurnaceRow> Furnaces { get; set; } = [];
}

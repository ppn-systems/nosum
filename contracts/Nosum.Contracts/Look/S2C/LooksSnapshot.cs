// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Look.S2C;

/// <summary>Reply to <see cref="C2S.GetLooks"/>: the look of every player on the map, the receiver included.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class LooksSnapshot : PacketBase<LooksSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => LookOpCode.LooksSnapshot;

    /// <summary>The looks.</summary>
    [SerializeOrder(0)] public List<LookRow> Looks { get; set; } = [];
}

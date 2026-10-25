// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Goals.S2C;

/// <summary>The goals of the current season and of the completed ones of other seasons.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GoalsSnapshot : PacketBase<GoalsSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => GoalOpCode.GoalsSnapshot;

    /// <summary>The server clock when read, Unix milliseconds.</summary>
    [SerializeOrder(0)] public long ServerNowMs { get; set; }

    /// <summary>When the current season ends, Unix milliseconds on the server clock.</summary>
    [SerializeOrder(1)] public long SeasonEndsAtMs { get; set; }

    /// <summary>The goals, in catalogue order.</summary>
    [SerializeOrder(2)] public List<GoalRow> Rows { get; set; } = [];
}

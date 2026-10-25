// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Farm.S2C;

/// <summary>Reply to <see cref="C2S.GetFarm"/>: every worked tile of one map and the server's clock.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class FarmSnapshot : PacketBase<FarmSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FarmOpCode.FarmSnapshot;

    /// <summary>The map the tiles belong to; empty when the player is on no map.</summary>
    [SerializeOrder(0)] public string MapId { get; set; } = string.Empty;

    /// <summary>The server's wall clock when the snapshot was made, Unix milliseconds; the client derives its offset from it.</summary>
    [SerializeOrder(1)] public long ServerNowMs { get; set; }

    /// <summary>Every tilled tile.</summary>
    [SerializeOrder(2)] public List<FarmTileRow> Tiles { get; set; } = [];
}

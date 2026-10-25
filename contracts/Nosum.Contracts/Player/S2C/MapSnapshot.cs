// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Player.S2C;

/// <summary>
/// Pushed to every player on a map about 10 times per second: the full state of everyone on it,
/// the receiver included. Full state (not deltas) keeps a lost or late snapshot harmless
/// (docs/adr/ADR-008).
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MapSnapshot : PacketBase<MapSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlayerOpCode.MapSnapshot;

    /// <summary>Server time of this snapshot in milliseconds on a monotonic clock; only differences are meaningful.</summary>
    [SerializeOrder(0)] public long ServerTimeMs { get; set; }

    /// <summary>Every player on the map.</summary>
    [SerializeOrder(1)] public List<PlayerState> Players { get; set; } = [];
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Requests.S2C;

/// <summary>Today's requests and the viewer's part in them.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RequestsSnapshot : PacketBase<RequestsSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => RequestsOpCode.RequestsSnapshot;

    /// <summary>The server clock when read, Unix milliseconds.</summary>
    [SerializeOrder(0)] public long ServerNowMs { get; set; }

    /// <summary>The game day number the requests belong to.</summary>
    [SerializeOrder(1)] public int Day { get; set; }

    /// <summary>When the day ends and new requests come, Unix milliseconds on the server clock.</summary>
    [SerializeOrder(2)] public long DayEndsAtMs { get; set; }

    /// <summary>The requests, by slot.</summary>
    [SerializeOrder(3)] public List<RequestRow> Rows { get; set; } = [];
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Coop.S2C;

/// <summary>The receiver's co-op state.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CoopSnapshot : PacketBase<CoopSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CoopOpCode.CoopSnapshot;

    /// <summary>Whether the receiver belongs to a co-op.</summary>
    [SerializeOrder(0)] public bool HasCoop { get; set; }

    /// <summary>The co-op's name as authored.</summary>
    [SerializeOrder(1)] public string Name { get; set; } = string.Empty;

    /// <summary>The receiver's role.</summary>
    [SerializeOrder(2)] public CoopRole Role { get; set; }

    /// <summary>The members.</summary>
    [SerializeOrder(3)] public List<CoopMemberRow> Members { get; set; } = [];

    /// <summary>The warehouse.</summary>
    [SerializeOrder(4)] public List<CoopStockRow> Stock { get; set; } = [];

    /// <summary>The latest warehouse log lines, newest first.</summary>
    [SerializeOrder(5)] public List<CoopLogRow> Log { get; set; } = [];

    /// <summary>Open invitations to the receiver.</summary>
    [SerializeOrder(6)] public List<CoopInviteRow> Invites { get; set; } = [];

    /// <summary>The server clock when read, Unix milliseconds.</summary>
    [SerializeOrder(7)] public long ServerNowMs { get; set; }
}

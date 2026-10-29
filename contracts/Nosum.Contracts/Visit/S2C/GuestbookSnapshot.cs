// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Visit.S2C;

/// <summary>The receiver's farm guestbook: how many entries were new when it was read, and the newest entries.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GuestbookSnapshot : PacketBase<GuestbookSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => VisitOpCode.GuestbookSnapshot;

    /// <summary>How many of the rows were new before this read.</summary>
    [SerializeOrder(0)] public int UnreadCount { get; set; }

    /// <summary>The entries, newest day first, at most <c>GuestbookRules.MaxRows</c>.</summary>
    [SerializeOrder(1)] public List<GuestbookRow> Rows { get; set; } = [];
}

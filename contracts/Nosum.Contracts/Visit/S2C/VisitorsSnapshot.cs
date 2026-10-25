// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Visit.S2C;

/// <summary>The receiver's allow-list settings and visitors.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class VisitorsSnapshot : PacketBase<VisitorsSnapshot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => VisitOpCode.VisitorsSnapshot;

    /// <summary>Whether members of the receiver's co-op may visit.</summary>
    [SerializeOrder(0)] public bool CoopMembersMayVisit { get; set; }

    /// <summary>Whether the receiver is a guest, who cannot keep an allow-list (docs/adr/ADR-019 D2).</summary>
    [SerializeOrder(1)] public bool IsGuest { get; set; }

    /// <summary>The allow-list, plus any co-op member who is visiting now.</summary>
    [SerializeOrder(2)] public List<VisitorRow> Visitors { get; set; } = [];
}

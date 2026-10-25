// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Admin.S2C;

/// <summary>The reply to <see cref="C2S.AdminReadReport"/>: the messages the reporter had received, oldest first.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AdminReportDetail : PacketBase<AdminReportDetail>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AdminOpCode.AdminReportDetail;

    /// <summary>Ok, or why there are no lines.</summary>
    [SerializeOrder(0)] public AdminStatus Status { get; set; }

    /// <summary>The stored messages.</summary>
    [SerializeOrder(1)] public List<AdminReportLineRow> Lines { get; set; } = [];
}

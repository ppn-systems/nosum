// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Admin.S2C;

/// <summary>The newest chat reports, newest first. Empty with a non-Ok status when the sender is not an admin.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AdminReportList : PacketBase<AdminReportList>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AdminOpCode.AdminReportList;

    /// <summary>Ok, or why the list is empty.</summary>
    [SerializeOrder(0)] public AdminStatus Status { get; set; }

    /// <summary>The reports.</summary>
    [SerializeOrder(1)] public List<AdminReportRow> Reports { get; set; } = [];
}

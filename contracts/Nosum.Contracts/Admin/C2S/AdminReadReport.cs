// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Admin.C2S;

/// <summary>Admin intent: asks for the stored context of one chat report.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AdminReadReport : PacketBase<AdminReadReport>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AdminOpCode.ReadReport;

    /// <summary>The report to read.</summary>
    [SerializeOrder(0)] public Guid ReportId { get; set; }
}

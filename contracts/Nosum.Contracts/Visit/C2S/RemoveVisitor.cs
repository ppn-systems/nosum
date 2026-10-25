// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Visit.C2S;

/// <summary>Removes a player from the allow-list and sends them out.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RemoveVisitor : PacketBase<RemoveVisitor>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => VisitOpCode.RemoveVisitor;

    /// <summary>The account id of the player, as listed in the snapshot.</summary>
    [SerializeOrder(0)] public Guid PlayerId { get; set; }
}

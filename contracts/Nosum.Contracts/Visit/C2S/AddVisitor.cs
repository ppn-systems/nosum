// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Visit.C2S;

/// <summary>Adds a player to the allow-list.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AddVisitor : PacketBase<AddVisitor>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => VisitOpCode.AddVisitor;

    /// <summary>The nickname the owner typed; the server resolves it.</summary>
    [SerializeOrder(0)] public string Nickname { get; set; } = string.Empty;
}

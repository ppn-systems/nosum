// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Visit.C2S;

/// <summary>Turns co-op members may visit on or off.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class SetCoopVisits : PacketBase<SetCoopVisits>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => VisitOpCode.SetCoopVisits;

    /// <summary>Whether members of the owner's co-op may visit.</summary>
    [SerializeOrder(0)] public bool Allowed { get; set; }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Fishing.C2S;

/// <summary>
/// Reels the line in, pressed when the bite indicator shows. The packet carries no timing: the server judges the press against its own
/// clock from the cast. Reply: <see cref="S2C.ReelResult"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ReelIn : PacketBase<ReelIn>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FishingOpCode.ReelIn;

    /// <summary>The cast id from <see cref="S2C.CastResult"/>.</summary>
    [SerializeOrder(0)] public uint CastId { get; set; }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Fishing.S2C;

/// <summary>
/// Reply to <see cref="C2S.CastRod"/>. When the status is Ok the cast is open: the bite comes <see cref="BiteDelayMs"/> milliseconds after
/// this reply was sent, and the press window stays open for <see cref="WindowMs"/> milliseconds after that.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CastResult : PacketBase<CastResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FishingOpCode.CastResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public FishingStatus Status { get; set; }

    /// <summary>The id of the open cast, for <see cref="C2S.ReelIn"/>; zero unless the status is Ok.</summary>
    [SerializeOrder(1)] public uint CastId { get; set; }

    /// <summary>Milliseconds from this reply to the bite; zero unless the status is Ok.</summary>
    [SerializeOrder(2)] public int BiteDelayMs { get; set; }

    /// <summary>Milliseconds the press window stays open after the bite; zero unless the status is Ok.</summary>
    [SerializeOrder(3)] public int WindowMs { get; set; }
}

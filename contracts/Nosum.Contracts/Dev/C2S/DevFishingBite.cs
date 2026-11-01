// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Dev.C2S;

/// <summary>Development only: makes the sender'\''s open fishing cast bite at once, so a reel-in in the window is a catch. Not handled in Production.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DevFishingBite : PacketBase<DevFishingBite>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => DevOpCode.DevFishingBite;

    /// <summary>The cast to make bite; it must be the sender's open cast.</summary>
    [SerializeOrder(0)] public uint CastId { get; set; }
}

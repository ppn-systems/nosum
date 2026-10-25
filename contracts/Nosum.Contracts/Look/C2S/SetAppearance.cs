// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Look.C2S;

/// <summary>Chooses the avatar parts.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class SetAppearance : PacketBase<SetAppearance>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => LookOpCode.SetAppearance;

    /// <summary>Skin tone id.</summary>
    [SerializeOrder(0)] public byte SkinId { get; set; }

    /// <summary>Hair style id.</summary>
    [SerializeOrder(1)] public byte HairStyleId { get; set; }

    /// <summary>Hair dye id.</summary>
    [SerializeOrder(2)] public byte HairDyeId { get; set; }
}

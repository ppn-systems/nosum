// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Admin.C2S;

/// <summary>Admin intent: stops a player from chatting for a number of hours.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AdminMute : PacketBase<AdminMute>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AdminOpCode.Mute;

    /// <summary>The nickname of the player to mute.</summary>
    [WireMaxLength(WireLimits.ShortText)]
    [SerializeOrder(0)] public string Nickname { get; set; } = string.Empty;

    /// <summary>How many hours the mute lasts (<see cref="AdminLimits.MaxMuteHours"/> at most); zero lifts the mute.</summary>
    [WireRange(0, AdminLimits.MaxMuteHours)]
    [SerializeOrder(1)] public int Hours { get; set; }
}

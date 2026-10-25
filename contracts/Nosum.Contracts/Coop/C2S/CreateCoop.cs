// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Coop.C2S;

/// <summary>Founds a co-op; the name is player-authored text, plain or an i18n envelope.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CreateCoop : PacketBase<CreateCoop>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CoopOpCode.CreateCoop;

    /// <summary>The name as typed.</summary>
    [WireMaxLength(WireLimits.ShortText)]
    [SerializeOrder(0)] public string Name { get; set; } = string.Empty;
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Farm.S2C;

/// <summary>Reply to <see cref="C2S.UseItem"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class UseItemResult : PacketBase<UseItemResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FarmOpCode.UseItemResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public FarmStatus Status { get; set; }

    /// <summary>What was done; <see cref="FarmAction.None"/> unless the status is <see cref="FarmStatus.Ok"/>.</summary>
    [SerializeOrder(1)] public FarmAction Action { get; set; }
}

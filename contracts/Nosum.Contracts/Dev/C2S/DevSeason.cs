// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Dev.C2S;

/// <summary>Development only: moves the server clock to the start of the next season, as many times as asked. Not handled in Production.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DevSeason : PacketBase<DevSeason>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => DevOpCode.DevSeason;

    /// <summary>How many season starts to pass, 1 to 4; zero passes to the next Spring (ADR-079 D3). The server refuses any other value.</summary>
    [WireRange(0, 4)]
    [SerializeOrder(0)] public int Seasons { get; set; } = 1;
}

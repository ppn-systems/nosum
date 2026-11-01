// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Dev.C2S;

/// <summary>
/// Development only: skips the server's game clock to the next day with sun (no rain, storm or snow). Weather is a pure function of the calendar
/// and the day (docs/adr/ADR-027 D1), so the tester moves the day rather than overriding the weather. Not handled in Production.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DevSunnyDay : PacketBase<DevSunnyDay>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => DevOpCode.DevSunnyDay;
}

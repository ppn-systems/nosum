// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Season.C2S;

/// <summary>Asks for the server calendar: its epoch, the length of a game day and the server's clock.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetCalendar : PacketBase<GetCalendar>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => SeasonOpCode.GetCalendar;
}

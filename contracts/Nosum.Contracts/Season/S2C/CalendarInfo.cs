// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Season.S2C;

/// <summary>
/// Reply to <see cref="C2S.GetCalendar"/>. Two numbers describe the whole calendar (<c>GameCalendar</c> in gameplay), so the client
/// computes the season, day and time itself; <see cref="ServerNowMs"/> lets it correct its own clock.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CalendarInfo : PacketBase<CalendarInfo>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => SeasonOpCode.CalendarInfo;

    /// <summary>Start of year 1, spring, day 1, 00:00 UTC, Unix milliseconds.</summary>
    [SerializeOrder(0)] public long EpochMs { get; set; }

    /// <summary>Real length of one game day, milliseconds.</summary>
    [SerializeOrder(1)] public long DayLengthMs { get; set; }

    /// <summary>The server's clock when it replied, Unix milliseconds.</summary>
    [SerializeOrder(2)] public long ServerNowMs { get; set; }
}

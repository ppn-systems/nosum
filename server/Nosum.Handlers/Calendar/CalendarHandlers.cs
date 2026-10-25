// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Calendar;
using Nosum.Contracts.Season;
using Nosum.Contracts.Season.C2S;
using Nosum.Contracts.Season.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Calendar;

/// <summary>The server calendar (docs/adr/ADR-018). The client asks once per sign-in and computes the season, day and time itself.</summary>
[PacketHandler("Nosum.Season")]
public static partial class CalendarHandlers
{
    [Inject] private static ICalendarService s_calendar = null!;

    /// <summary>Replies with the calendar's epoch, the game day length and the server clock.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(SeasonOpCode.GetCalendar)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 4, PolicyId = "calendar-read")]
    public static ValueTask HandleGetCalendarAsync(IPacketContext<GetCalendar> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        return Replies.ReplyAsync<GetCalendar, CalendarInfo>(context, sequence, r =>
        {
            r.EpochMs = s_calendar.Calendar.EpochMs;
            r.DayLengthMs = s_calendar.Calendar.DayLengthMs;
            r.ServerNowMs = s_calendar.NowMs();
        });
    }
}

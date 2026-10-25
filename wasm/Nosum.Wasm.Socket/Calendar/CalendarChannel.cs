// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Season.C2S;
using Nosum.Contracts.Season.S2C;

namespace Nosum.Wasm.Socket.Calendar;

/// <summary>
/// The client's copy of the server calendar (docs/adr/ADR-018): the epoch and the game day length, read once at sign-in, plus an estimate of
/// the server's clock. The season, day and time are computed locally from these (the shared <c>GameCalendar</c>); the server pushes nothing.
/// </summary>
public sealed class CalendarChannel
{
    private readonly IGameSocket _socket;
    private long _epochMs;
    private long _dayLengthMs;
    private long _clockOffsetMs;

    /// <summary>Initializes the channel.</summary>
    /// <param name="socket">The game connection.</param>
    public CalendarChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
    }

    /// <summary>Raised when the calendar was read. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Gets a value indicating whether the calendar was read from the server.</summary>
    public bool IsLoaded => Interlocked.Read(ref _dayLengthMs) > 0;

    /// <summary>Gets the start of year 1, Unix milliseconds; meaningful once <see cref="IsLoaded"/>.</summary>
    public long EpochMs => Interlocked.Read(ref _epochMs);

    /// <summary>Gets the real length of a game day, milliseconds; zero until loaded.</summary>
    public long DayLengthMs => Interlocked.Read(ref _dayLengthMs);

    /// <summary>Estimates the server's wall clock now, Unix milliseconds (the local clock plus the offset the server revealed).</summary>
    /// <returns>The estimate.</returns>
    public long ServerNowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Interlocked.Read(ref _clockOffsetMs);

    /// <summary>Reads the calendar. Call whenever the connection is signed in.</summary>
    public void Load() => _ = this.LoadAsync();

    [SuppressMessage("Design", "CA1031", Justification = "A failed read is retried at the next sign-in; there is no caller to report to.")]
    private async Task LoadAsync()
    {
        try
        {
            using PacketScope<GetCalendar> request = PacketFactory<GetCalendar>.Acquire();
            CalendarInfo info = await _socket.RequestAsync<CalendarInfo>(request.Value).ConfigureAwait(false);
            Interlocked.Exchange(ref _epochMs, info.EpochMs);
            Interlocked.Exchange(ref _clockOffsetMs, info.ServerNowMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Interlocked.Exchange(ref _dayLengthMs, info.DayLengthMs);
            this.Changed?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
    }
}

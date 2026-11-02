// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Energy;
using Nosum.Wasm.Socket.Calendar;

namespace Nosum.Wasm.Socket.Player;

/// <summary>
/// The client's mirror of the player's energy and skill experience (docs/adr/ADR-023). The server owns both: it pushes <see cref="EnergyChanged"/> and
/// <see cref="SkillXpChanged"/> after joining and after every change, and <see cref="LevelUp"/> when a skill reaches a new level. Eating is an intent;
/// the answer arrives as <see cref="EatCompleted"/> and the new energy as a push.
/// </summary>
public sealed class ProgressChannel : IDisposable
{
    private const long NoReadDay = long.MinValue;

    private readonly IGameSocket _socket;
    private readonly IDisposable[] _subscriptions;
    private readonly CalendarChannel? _clock;
    private readonly Timer _dayTurn;
    private GameCalendar? _calendar;
    private int _energy;
    private int _maxEnergy;
    private long _readDay = NoReadDay;
    private int _farmingXp;
    private int _foragingXp;
    private int _miningXp;
    private int _fishingXp;

    /// <summary>Initializes the channel and starts listening for pushes.</summary>
    /// <param name="socket">The game connection.</param>
    /// <param name="calendar">The server calendar, for the game day. Energy rolls over to full when the day changes (docs/adr/ADR-114 D1); without it the
    /// energy shown is the last push.</param>
    public ProgressChannel(IGameSocket socket, CalendarChannel? calendar = null)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
        _clock = calendar;
        _dayTurn = new Timer(static state => ((ProgressChannel)state!).OnDayTurn(), this, Timeout.Infinite, Timeout.Infinite);
        _subscriptions =
        [
            socket.Subscribe<EnergyChanged>(this.OnEnergy),
            socket.Subscribe<SkillXpChanged>(this.OnXp),
            socket.Subscribe<LevelUp>(this.OnLevelUp),
        ];
        if (calendar is not null)
        {
            calendar.Changed += this.OnCalendarLoaded;
            this.OnCalendarLoaded();
        }
    }

    /// <summary>Raised when the energy or the experience changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised when a skill reached a new level: the skill (1 farming, 2 foraging, 3 mining) and the level. May run on any thread.</summary>
    public event Action<byte, int>? LevelledUp;

    /// <summary>Raised when the server answered an eat request. May run on any thread.</summary>
    public event Action<EatStatus>? EatCompleted;

    /// <summary>Gets the energy now; zero until the server has said. After the game day changes the energy is full, as the server refills on its next read (docs/adr/ADR-114 D1).</summary>
    public int Energy
    {
        get
        {
            long readDay = Volatile.Read(ref _readDay);
            return EnergyRules.Shown(Volatile.Read(ref _energy), Volatile.Read(ref _maxEnergy), readDay == NoReadDay ? null : readDay, this.Today());
        }
    }

    /// <summary>Gets the largest energy; zero until the server has said, which hides the bar.</summary>
    public int MaxEnergy => Volatile.Read(ref _maxEnergy);

    /// <summary>Gets the cumulative farming experience.</summary>
    public int FarmingXp => Volatile.Read(ref _farmingXp);

    /// <summary>Gets the cumulative foraging experience.</summary>
    public int ForagingXp => Volatile.Read(ref _foragingXp);

    /// <summary>Gets the cumulative mining experience.</summary>
    public int MiningXp => Volatile.Read(ref _miningXp);

    /// <summary>Gets the cumulative fishing experience (docs/adr/ADR-078 amendment).</summary>
    public int FishingXp => Volatile.Read(ref _fishingXp);

    /// <summary>Asks the server to eat the food in a hotbar slot; the answer arrives as <see cref="EatCompleted"/>.</summary>
    /// <param name="slot">Hotbar slot, zero to eight.</param>
    public void Eat(int slot) => _ = this.EatAsync(slot);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_clock is not null)
        {
            _clock.Changed -= this.OnCalendarLoaded;
        }

        _dayTurn.Dispose();
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    private long Today()
    {
        GameCalendar? calendar = Volatile.Read(ref _calendar);
        return calendar is null || _clock is null ? 0 : calendar.DayNumberAt(_clock.ServerNowMs());
    }

    private void OnCalendarLoaded()
    {
        if (_clock is not { IsLoaded: true } clock)
        {
            return;
        }

        Volatile.Write(ref _calendar, new GameCalendar(clock.EpochMs, clock.DayLengthMs));

        // A reading that arrived before the calendar was known was taken on the day the calendar loaded: sign-in reads both at once.
        _ = Interlocked.CompareExchange(ref _readDay, this.Today(), NoReadDay);
        this.ScheduleDayTurn();
        this.Changed?.Invoke();
    }

    private void ScheduleDayTurn()
    {
        GameCalendar? calendar = Volatile.Read(ref _calendar);
        if (calendar is null || _clock is null)
        {
            return;
        }

        long now = _clock.ServerNowMs();

        // A small margin past the boundary: the clock is an estimate, and a turn that fires early re-arms itself for the rest of the gap.
        long dueMs = Math.Max(250, calendar.NextDayStartMs(now) - now + 50);
        _ = _dayTurn.Change(dueMs, Timeout.Infinite);
    }

    private void OnDayTurn()
    {
        // The day may have turned: the shown energy is full from now on, so the HUD repaints. Then the next turn is armed.
        this.Changed?.Invoke();
        this.ScheduleDayTurn();
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task EatAsync(int slot)
    {
        try
        {
            using PacketScope<Eat> request = PacketFactory<Eat>.Acquire();
            request.Value.Slot = (byte)slot;
            EatResult result = await _socket.RequestAsync<EatResult>(request.Value).ConfigureAwait(false);
            this.EatCompleted?.Invoke(result.Status);
        }
        catch (Exception)
        {
            // Connection dropped; nothing was eaten.
        }
    }

    private void OnEnergy(EnergyChanged packet)
    {
        Volatile.Write(ref _energy, packet.Energy);
        Volatile.Write(ref _maxEnergy, packet.MaxEnergy);
        Volatile.Write(ref _readDay, Volatile.Read(ref _calendar) is null ? NoReadDay : this.Today());
        this.ScheduleDayTurn();
        this.Changed?.Invoke();
    }

    private void OnXp(SkillXpChanged packet)
    {
        Volatile.Write(ref _farmingXp, packet.FarmingXp);
        Volatile.Write(ref _foragingXp, packet.ForagingXp);
        Volatile.Write(ref _miningXp, packet.MiningXp);
        Volatile.Write(ref _fishingXp, packet.FishingXp);
        this.Changed?.Invoke();
    }

    private void OnLevelUp(LevelUp packet) => this.LevelledUp?.Invoke(packet.Skill, packet.Level);
}

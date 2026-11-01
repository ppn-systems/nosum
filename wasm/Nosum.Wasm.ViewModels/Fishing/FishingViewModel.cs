// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Fishing;
using Nosum.Contracts.Fishing.S2C;
using Nosum.Gameplay.Fishing;
using Nosum.Wasm.Socket.Fishing;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Fishing;

/// <summary>
/// The fishing banner over the hotbar (docs/adr/ADR-078 D4, D9). The client decides nothing that counts: it sends the cast, shows the bite and the
/// press window the server reported, sends the reel-in on the press and shows the reply. The one timer only moves the banner from step to step; the
/// server judges the press on its own clock, so a late timer never changes a catch.
/// </summary>
public sealed class FishingViewModel : IObservableViewModel, IDisposable
{
    /// <summary>How long a result stays on the banner before it clears by itself.</summary>
    public static readonly TimeSpan ResultLinger = TimeSpan.FromSeconds(4);

    /// <summary>How long the banner waits before it looks at the bite window again after a press that came too early.</summary>
    public static readonly TimeSpan TooEarlyRetry = TimeSpan.FromMilliseconds(300);

    private static readonly TimerCallback s_fire = Fire;

    private readonly FishingChannel _channel;
    private readonly ItemDescriber _items;
    private readonly IStringLocalizer<Language> _text;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();

    // One timer drives the banner: the step it runs next. A callback that belongs to an earlier timer carries an old id and is ignored.
    private Step _next;
    private ITimer? _timer;
    private int _timerId;

    private FishingPhase _phase;
    private int _generation;
    private uint _castId;
    private long _biteAtMs;
    private int _windowMs;
    private int _barMs;
    private int _castTileX;
    private int _castTileY;
    private FishCatch? _catch;
    private string _result = string.Empty;
    private string _note = string.Empty;

    /// <summary>Initializes the view model.</summary>
    /// <param name="channel">Sends casts and reel-ins.</param>
    /// <param name="items">Names the caught fish.</param>
    /// <param name="text">UI text.</param>
    /// <param name="time">The clock the bite and window timers run on; the system clock when <see langword="null"/>. Tests pass their own.</param>
    public FishingViewModel(FishingChannel channel, ItemDescriber items, IStringLocalizer<Language> text, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(text);
        _channel = channel;
        _items = items;
        _text = text;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised when the banner must render again. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>
    /// The cast a line is waiting on for its bite, zero when none is. The tester hook (docs/adr/ADR-079) reads it to ask the server for a bite.
    /// </summary>
    public uint OpenCastId
    {
        get
        {
            lock (_gate)
            {
                return _phase == FishingPhase.Waiting ? _castId : 0;
            }
        }
    }

    /// <summary>
    /// Tester hook (docs/adr/ADR-079): the server has made the waiting cast bite, so the bite cue shows now instead of after the delay the cast
    /// reply named. Does nothing unless a line is waiting for its bite.
    /// </summary>
    public void BiteNow()
    {
        lock (_gate)
        {
            if (_phase != FishingPhase.Waiting)
            {
                return;
            }

            _biteAtMs = this.NowMs();
            this.Arm(Step.Bite, 0);
        }
    }

    /// <summary>Gets where the banner is in the cast.</summary>
    public FishingPhase Phase
    {
        get
        {
            lock (_gate)
            {
                return _phase;
            }
        }
    }

    /// <summary>Gets whether a line is out in the water: a press (a click or Space) then reels it in, and the world does not take the click.</summary>
    public bool LineOut
    {
        get
        {
            lock (_gate)
            {
                return _phase is FishingPhase.Waiting or FishingPhase.Biting;
            }
        }
    }

    /// <summary>Gets how long the bite window bar has left to run, in milliseconds; zero outside the window.</summary>
    public int BarMs
    {
        get
        {
            lock (_gate)
            {
                return _barMs;
            }
        }
    }

    /// <summary>Gets the fish of the last catch, for the catch card; null until a fish is reeled in and again after the next cast starts.</summary>
    public FishCatch? Catch
    {
        get
        {
            lock (_gate)
            {
                return _catch;
            }
        }
    }

    /// <summary>Reads what the fishing scene draws this frame, in one lock.</summary>
    /// <returns>The phase, the window's time left and length, and the tile the line went into.</returns>
    public FishingFrame Read()
    {
        lock (_gate)
        {
            return new FishingFrame(_phase, _barMs, _windowMs, _castTileX, _castTileY);
        }
    }

    /// <summary>Gets the banner heading, <c>Fishing</c>.</summary>
    public string Title => this.Text("Fishing_Title");

    /// <summary>Gets or sets whether the player's primary input is a finger. A finger gets the tap prompt, a keyboard or mouse the Space-or-click
    /// prompt (docs/adr/ADR-092). The shell sets it once, from the device, before the first frame.</summary>
    public bool TouchInput { get; set; }

    /// <summary>Gets the prompt shown while the bite window is open: <c>Tap</c> on touch, <c>Space or click</c> otherwise.</summary>
    public string PressPrompt => this.Text(this.TouchInput ? "Fishing_PressTouch" : "Fishing_Press");

    /// <summary>Gets the heading of the catch card, <c>You caught a fish!</c>.</summary>
    public string CatchTitle => this.Text("Fishing_CatchTitle");

    /// <summary>Gets the size line of a catch card, such as <c>32 cm long</c>.</summary>
    /// <param name="fish">The catch.</param>
    public string SizeOf(FishCatch fish)
    {
        ArgumentNullException.ThrowIfNull(fish);
        return string.Format(CultureInfo.CurrentCulture, this.Text("Fishing_Size"), fish.SizeCm);
    }

    /// <summary>Gets the rarity name of a catch card, such as <c>Rare</c>.</summary>
    /// <param name="fish">The catch.</param>
    public string RarityOf(FishCatch fish)
    {
        ArgumentNullException.ThrowIfNull(fish);
        return this.Text("Fishing_Rarity_" + fish.Rarity);
    }

    /// <summary>Gets the line for the current phase: casting, waiting, the bite, reeling, or the result.</summary>
    public string Message
    {
        get
        {
            lock (_gate)
            {
                return _phase switch
                {
                    FishingPhase.Casting => this.Text("Fishing_Casting"),
                    FishingPhase.Waiting => this.Text("Fishing_Waiting"),
                    FishingPhase.Biting => this.Text("Fishing_Bite"),
                    FishingPhase.Reeling => this.Text("Fishing_Reeling"),
                    FishingPhase.Result => _result,
                    _ => string.Empty,
                };
            }
        }
    }

    /// <summary>Gets a short hint under the line, such as "not yet" after a press during the wait; empty when there is none.</summary>
    public string Note
    {
        get
        {
            lock (_gate)
            {
                return _note;
            }
        }
    }

    /// <summary>
    /// Casts the held rod at a water tile. Only an idle banner or a finished cast takes a cast; while a line is out or a cast is in flight the call
    /// does nothing. The server checks the rod, the water, the reach and the energy.
    /// </summary>
    /// <param name="tileX">Tile x of the water.</param>
    /// <param name="tileY">Tile y of the water.</param>
    public void Cast(int tileX, int tileY)
    {
        int generation;
        lock (_gate)
        {
            if (_phase is not (FishingPhase.Idle or FishingPhase.Result))
            {
                return;
            }

            this.Disarm();
            generation = ++_generation;
            _phase = FishingPhase.Casting;
            _result = string.Empty;
            _note = string.Empty;
            _barMs = 0;
            _castTileX = tileX;
            _castTileY = tileY;
            _catch = null;
        }

        this.Changed?.Invoke();
        _ = this.SendCastAsync(generation, tileX, tileY);
    }

    /// <summary>
    /// The press: during the bite window it reels the line in (the server judges it); during the wait it says the fish has not bitten yet. At any
    /// other time it does nothing.
    /// </summary>
    public void Press()
    {
        int generation = 0;
        uint castId = 0;
        bool reel = false;
        lock (_gate)
        {
            if (_phase == FishingPhase.Biting)
            {
                this.Disarm();
                _phase = FishingPhase.Reeling;
                _note = string.Empty;
                generation = _generation;
                castId = _castId;
                reel = true;
            }
            else if (_phase == FishingPhase.Waiting)
            {
                _note = this.Text("Fishing_NotYet");
            }
        }

        this.Changed?.Invoke();
        if (reel)
        {
            _ = this.SendReelAsync(generation, castId);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            this.Disarm();
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing on the server; the banner says the line did not go out and the player casts again.")]
    private async Task SendCastAsync(int generation, int tileX, int tileY)
    {
        CastResult? reply = null;
        try
        {
            reply = await _channel.CastAsync(tileX, tileY).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The connection dropped: the reply stays null and the cast is reported as a conflict.
        }

        lock (_gate)
        {
            this.ApplyCast(generation, reply);
        }

        this.Changed?.Invoke();
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing on the server; the banner says the press did not go through and the player tries again.")]
    private async Task SendReelAsync(int generation, uint castId)
    {
        ReelResult? reply = null;
        try
        {
            reply = await _channel.ReelAsync(castId).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The connection dropped: the reply stays null and the reel-in is reported as a conflict.
        }

        lock (_gate)
        {
            this.ApplyReel(generation, reply);
        }

        this.Changed?.Invoke();
    }

    // Call under the lock. A reply that belongs to a cast the player has since replaced is dropped.
    private void ApplyCast(int generation, CastResult? reply)
    {
        if (generation != _generation || _phase != FishingPhase.Casting)
        {
            return;
        }

        if (reply is { Status: FishingStatus.Ok })
        {
            // The server's clock started with its reply, and ours starts with the one we just received: the window opens a little after the
            // server's, and the server's late tolerance covers the difference.
            _castId = reply.CastId;
            _windowMs = reply.WindowMs;
            _biteAtMs = this.NowMs() + reply.BiteDelayMs;
            _phase = FishingPhase.Waiting;
            this.Arm(Step.Bite, reply.BiteDelayMs);
            return;
        }

        this.Finish(this.StatusText(reply?.Status ?? FishingStatus.Conflict));
    }

    // Call under the lock. A press that came too early keeps the cast open, so the window opens again after a short wait.
    private void ApplyReel(int generation, ReelResult? reply)
    {
        if (generation != _generation || _phase != FishingPhase.Reeling)
        {
            return;
        }

        if (reply is null)
        {
            this.Finish(this.StatusText(FishingStatus.Conflict));
            return;
        }

        switch (reply.Status)
        {
            case FishingStatus.Ok when reply.ItemId > 0:
                _catch = this.CatchOf(reply);
                this.Finish(string.Format(CultureInfo.CurrentCulture, this.Text("Fishing_Caught"), _items.NameOf(reply.ItemId)));
                break;
            case FishingStatus.Ok:
            case FishingStatus.Nothing:
                this.Finish(this.StatusText(FishingStatus.Nothing));
                break;
            case FishingStatus.TooEarly:
                _phase = FishingPhase.Waiting;
                _note = this.StatusText(FishingStatus.TooEarly);
                this.Arm(Step.Bite, (long)TooEarlyRetry.TotalMilliseconds);
                break;
            default:
                this.Finish(this.StatusText(reply.Status));
                break;
        }
    }

    // Call under the lock. Opens the bite window for the time that is left of it; a window already over means the fish got away.
    private void OpenWindow()
    {
        long now = this.NowMs();
        long end = _biteAtMs + _windowMs;
        if (now >= end)
        {
            this.Finish(this.StatusText(FishingStatus.Escaped));
            return;
        }

        _phase = FishingPhase.Biting;
        _note = string.Empty;
        _barMs = (int)(end - now);
        this.Arm(Step.Close, end - now);
    }

    // Call under the lock. The fish of a catch, with its name, size and rarity; a fish the catalogue does not know is shown as common.
    private FishCatch CatchOf(ReelResult reply)
    {
        FishRarity rarity = FishCatalog.TryGet(reply.ItemId, out FishSpecies? species) ? species.Rarity : FishRarity.Common;
        return new FishCatch(reply.ItemId, _items.NameOf(reply.ItemId), reply.SizeCm, rarity);
    }

    // Call under the lock. Ends the cast on a result, which stays for a while before the banner clears.
    private void Finish(string text)
    {
        _phase = FishingPhase.Result;
        _result = text;
        _note = string.Empty;
        _barMs = 0;
        this.Arm(Step.Linger, (long)ResultLinger.TotalMilliseconds);
    }

    // Runs the step when its timer fires. Takes the lock itself: the timer runs on its own thread.
    private void OnTimer(int id)
    {
        lock (_gate)
        {
            if (id != _timerId || _next == Step.None)
            {
                return;
            }

            Step step = _next;
            _next = Step.None;
            _timer?.Dispose();
            _timer = null;
            switch (step)
            {
                case Step.Bite when _phase == FishingPhase.Waiting:
                    this.OpenWindow();
                    break;
                case Step.Close when _phase == FishingPhase.Biting:
                    this.Finish(this.StatusText(FishingStatus.Escaped));
                    break;
                case Step.Linger when _phase == FishingPhase.Result:
                    _phase = FishingPhase.Idle;
                    _result = string.Empty;
                    break;
            }
        }

        this.Changed?.Invoke();
    }

    // Call under the lock. Replaces the pending step with a new one, due after the delay.
    private void Arm(Step step, long delayMs)
    {
        this.Disarm();
        _next = step;
        _timer = _time.CreateTimer(s_fire, new Tick(this, _timerId), TimeSpan.FromMilliseconds(Math.Max(0, delayMs)), Timeout.InfiniteTimeSpan);
    }

    // Call under the lock. Drops the pending step; a callback already on its way finds an old id and does nothing.
    private void Disarm()
    {
        _next = Step.None;
        _timerId++;
        _timer?.Dispose();
        _timer = null;
    }

    private static void Fire(object? state)
    {
        Tick tick = (Tick)state!;
        tick.Owner.OnTimer(tick.Id);
    }

    private long NowMs() => _time.GetUtcNow().ToUnixTimeMilliseconds();

    private string StatusText(FishingStatus status) => this.Text("Fishing_Status_" + status);

    private string Text(string key) => _text[key].Value;

    // What a timer does next. One at a time: each change of phase replaces it.
    private enum Step : byte
    {
        None = 0,
        Bite = 1,
        Close = 2,
        Linger = 3,
    }

    // The callback's state: the view model and the timer id it was armed with.
    private sealed record Tick(FishingViewModel Owner, int Id);
}

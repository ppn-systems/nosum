// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Weather;
using Nosum.Rendering;
using Nosum.Rendering.Web;
using Nosum.Wasm.ViewModels.Audio;

namespace Nosum.Wasm.Game;

/// <summary>
/// The client's audio director (docs/adr/ADR-039): the hooks ask it for sounds (<see cref="Play"/>), the scene tells it once per frame what is going on
/// (<see cref="Frame"/>: footsteps, the music of the season and hour, the rain loop) and writes its queue into the frame's command buffer (<see cref="Flush"/>). A sound
/// is a command in the buffer that already crosses to the browser every frame, so audio costs no crossing of its own. Allocation-free after construction.
/// </summary>
internal sealed class GameAudio
{
    private const int FootstepVolume = 70;

    private readonly SoundCues _queue = new();
    private FootstepTimer _steps;
    private double _now;
    private int _listenerX;
    private int _listenerY;

    /// <summary>Initializes the director with the minimum interval of every sound of <see cref="AudioCatalog.Files"/>.</summary>
    public GameAudio()
    {
        foreach (AudioFile file in AudioCatalog.Files)
        {
            if (file.Kind == AudioKind.Sound && file.MinIntervalSeconds > 0)
            {
                _queue.SetMinInterval(file.Id, file.MinIntervalSeconds);
            }
        }
    }

    /// <summary>
    /// Loads the sound effects and ambient loops of <see cref="AudioCatalog.Files"/> and tells the browser which sound a button click plays. Music tracks are
    /// only remembered here: the browser fetches each one when the game first asks for it (docs/adr/ADR-086 D4). Never throws: a file that does not load is silence.
    /// </summary>
    /// <returns>A task that completes when every file was tried.</returns>
    [SupportedOSPlatform("browser")]
    [SuppressMessage("Design", "CA1031", Justification = "Audio is a nicety: a failed load leaves the game silent, never broken.")]
    public static async Task LoadAsync()
    {
        WebRenderBackend.SetUiClick(SoundIds.UiClick);
        List<Task<bool>> loading = [];
        foreach (AudioFile file in AudioCatalog.Files)
        {
            loading.Add(WebRenderBackend.LoadAudioAsync((int)file.Kind, file.Id, AudioCatalog.Folder + file.File, file.GainDb));
        }

        try
        {
            _ = await Task.WhenAll(loading).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Audio is a nicety: a failed load leaves the game silent, never broken.
        }
    }

    /// <summary>Asks for a sound at the next frame.</summary>
    /// <param name="soundId">A <see cref="SoundIds"/> value; 0 does nothing.</param>
    /// <param name="volume">Volume, 0 to 100.</param>
    /// <param name="pan">Stereo position, -100 to 100.</param>
    public void Play(int soundId, int volume = 100, int pan = 0)
    {
        if (soundId > 0)
        {
            _ = _queue.Play(soundId, _now, volume, pan);
        }
    }

    /// <summary>Plays the pickup sound for an item that flew to a farmer standing at a place: loud when it is the local farmer's, quiet and panned when a neighbour's.</summary>
    /// <param name="takerX">Where the taker stood, map pixels.</param>
    /// <param name="takerY">Where the taker stood, map pixels.</param>
    public void PlayPickup(int takerX, int takerY)
    {
        if (AudioCatalog.PickupCue(takerX - _listenerX, takerY - _listenerY, out int volume, out int pan))
        {
            this.Play(SoundIds.Pickup, volume, pan);
        }
    }

    /// <summary>Called once per frame, before <see cref="Flush"/>: advances the clock and decides footsteps, music and the rain loop.</summary>
    /// <param name="frameSeconds">Wall time since the previous frame.</param>
    /// <param name="farmerX">The local farmer's feet, map pixels.</param>
    /// <param name="farmerY">The local farmer's feet, map pixels.</param>
    /// <param name="walking">Whether the farmer is walking (and not frozen by a map change).</param>
    /// <param name="indoors">Whether the map shown is indoors.</param>
    /// <param name="hasSky">Whether the server calendar is known; without it there is no music and no rain.</param>
    /// <param name="season">The server season.</param>
    /// <param name="minuteOfDay">The server game minute of the day.</param>
    /// <param name="weather">Today's weather.</param>
    /// <param name="mineFloor">The mine floor shown, zero when the map is not one; a mine floor plays its biome's track instead of the season's (docs/adr/ADR-070 D3).</param>
    public void Frame(double frameSeconds, int farmerX, int farmerY, bool walking, bool indoors, bool hasSky, Season season, int minuteOfDay, WeatherKind weather, int mineFloor)
    {
        _now += frameSeconds;
        _listenerX = farmerX;
        _listenerY = farmerY;
        if (_steps.Advance(walking, frameSeconds))
        {
            this.Play(SoundIds.Footstep, FootstepVolume);
        }

        int track = mineFloor >= 1
            ? MusicTrackIds.ForMine(MineBiomeRules.Of(mineFloor))
            : hasSky ? MusicTrackIds.For(season, AudioCatalog.IsNight(minuteOfDay)) : MusicTrackIds.None;
        _queue.SetMusic(track);
        int loopVolume = 0;
        int loop = hasSky ? AudioCatalog.AmbientLoopFor(weather, indoors, out loopVolume) : LoopIds.None;
        _queue.SetAmbientLoop(loop, loopVolume);
    }

    /// <summary>Writes the waiting sounds and changes into a frame that was just begun.</summary>
    /// <param name="commands">The frame's command buffer.</param>
    public void Flush(RenderCommandBuffer commands) => _ = _queue.Flush(commands);
}

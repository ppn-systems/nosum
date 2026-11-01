// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Contracts.Farm;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Weather;

namespace Nosum.Wasm.ViewModels.Audio;

/// <summary>What kind of audio an <see cref="AudioFile"/> is; the numbers are the ones <c>audio.js</c> expects.</summary>
public enum AudioKind
{
    /// <summary>A one-shot sound effect.</summary>
    Sound = 0,

    /// <summary>A seamless ambient loop (rain).</summary>
    Loop = 1,

    /// <summary>A music track.</summary>
    Track = 2,
}

/// <summary>One audio file the client loads at start (docs/adr/ADR-039, mix in docs/adr/ADR-090).</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Id">The id the frame's audio commands use (unique within its kind).</param>
/// <param name="File">The file name under <see cref="AudioCatalog.Folder"/>.</param>
/// <param name="MinIntervalSeconds">How soon the same sound may play again; 0 for no limit.</param>
/// <param name="MeasuredDbfs">The file's measured level: RMS over its active samples, in dBFS (<c>tools/measure_audio.py</c>).</param>
/// <param name="TargetDbfs">The level the mix wants the file at, from <see cref="AudioMix"/>.</param>
public sealed record AudioFile(AudioKind Kind, int Id, string File, double MinIntervalSeconds, double MeasuredDbfs, double TargetDbfs)
{
    /// <summary>Gets the gain the browser applies once at load to bring the file to its target, in decibels.</summary>
    public double GainDb => TargetDbfs - MeasuredDbfs;
}

/// <summary>
/// The mix table (docs/adr/ADR-090): one target level per class of sound, so the footsteps, the tools, the interface and the music sit in a known
/// range. Each file is measured, and the browser applies its gain once when it loads (<c>audio.js</c>); per-cue volumes (the hooks) still scale on top.
/// The levels are RMS over the active part of a file, in dBFS, before the bus gains of the settings.
/// </summary>
public static class AudioMix
{
    /// <summary>Music tracks: the bed of the game.</summary>
    public const double Music = -16.0;

    /// <summary>Tools, harvest, planting, pick-ups, coins, doors, combat and food: the cues the player acts on.</summary>
    public const double Action = -16.0;

    /// <summary>Stingers and rare cues (level up, combo finisher, doorbell, mail, craft, thunder): heard over the music when they play.</summary>
    public const double Stinger = -16.0;

    /// <summary>Footsteps: felt under the tools, and about 3.6 decibels under the music at the hook's volume (70; docs/adr/ADR-090 amendment).</summary>
    public const double Footstep = -16.5;

    /// <summary>Interface clicks, panel open and close: short and quiet.</summary>
    public const double Interface = -18.0;

    /// <summary>The rain loop: a bed at the music's level (it plays at 55 for rain, about 5 dB under the music, or 85 for a storm, about 1 dB under).</summary>
    public const double Ambience = -16.0;
}

/// <summary>The sound effect ids (docs/adr/ADR-039 D3).</summary>
public static class SoundIds
{
    /// <summary>The hoe's swing.</summary>
    public const int ToolHoe = 1;

    /// <summary>The watering can.</summary>
    public const int ToolWater = 2;

    /// <summary>The axe's chop.</summary>
    public const int ToolAxe = 3;

    /// <summary>The pickaxe's clink.</summary>
    public const int ToolPickaxe = 4;

    /// <summary>The scythe's swish (also the milk pail's).</summary>
    public const int ToolScythe = 5;

    /// <summary>A seed or sapling set in the ground.</summary>
    public const int Plant = 6;

    /// <summary>A crop harvested.</summary>
    public const int Harvest = 7;

    /// <summary>An item picked up from the ground.</summary>
    public const int Pickup = 8;

    /// <summary>The coin balance changed.</summary>
    public const int Coin = 9;

    /// <summary>A door or warp: the map changes.</summary>
    public const int Door = 10;

    /// <summary>A click on an interface button (played by the browser, not queued).</summary>
    public const int UiClick = 11;

    /// <summary>A panel opens.</summary>
    public const int UiOpen = 12;

    /// <summary>A panel closes.</summary>
    public const int UiClose = 13;

    /// <summary>A footstep.</summary>
    public const int Footstep = 14;

    /// <summary>A sword slash (docs/adr/ADR-053).</summary>
    public const int SwordSlash = 15;

    /// <summary>An arcane staff spell cast.</summary>
    public const int StaffCast = 16;

    /// <summary>A monster takes damage.</summary>
    public const int MonsterHit = 17;

    /// <summary>A monster is defeated.</summary>
    public const int MonsterDefeat = 18;

    /// <summary>A potion is drunk.</summary>
    public const int PotionDrink = 19;

    /// <summary>A celebratory level-up fanfare (docs/adr/ADR-062).</summary>
    public const int LevelUp = 20;

    /// <summary>A deep bass thump when a combat combo reaches its finisher (docs/adr/ADR-060).</summary>
    public const int ComboFinisher = 21;

    /// <summary>A low rumble after a lightning flash in a storm (docs/adr/ADR-039 D3).</summary>
    public const int Thunder = 22;

    /// <summary>A craft or a furnace batch finished: two anvil clinks.</summary>
    public const int Craft = 23;

    /// <summary>Food eaten: a soft crunch.</summary>
    public const int Eat = 24;

    /// <summary>A visitor arrives at the farm: a two-note doorbell.</summary>
    public const int Doorbell = 25;

    /// <summary>New mail or a friend's answer: a soft chime.</summary>
    public const int MailChime = 26;
}

/// <summary>The ambient loop ids.</summary>
public static class LoopIds
{
    /// <summary>No loop.</summary>
    public const int None = 0;

    /// <summary>Rain on the ground, for rain and storms outdoors.</summary>
    public const int Rain = 1;
}

/// <summary>The music track ids: one per season for the day and one for the night. Each has its own loop made by <c>tools/synth_music.py</c> (docs/adr/ADR-039 D6); a track without a file is silence.</summary>
public static class MusicTrackIds
{
    /// <summary>No music.</summary>
    public const int None = 0;

    /// <summary>Returns the track for a season and time of day.</summary>
    /// <param name="season">The season.</param>
    /// <param name="night">Whether it is night.</param>
    /// <returns>A track id, 1 to 8: 1 spring day, 2 spring night, 3 summer day, and so on.</returns>
    public static int For(Season season, bool night) => 1 + ((int)season * 2) + (night ? 1 : 0);

    /// <summary>
    /// Returns the track of a mine biome (docs/adr/ADR-070 D3): the four existing tracks that differ most from one another, so each depth tier has
    /// its own theme without a new file. Earth plays spring day, Frost winter day, Magma summer night and Ruins fall night.
    /// </summary>
    /// <param name="biome">The biome of the floor.</param>
    /// <returns>A track id, 1 to 8.</returns>
    public static int ForMine(MineBiome biome) => biome switch
    {
        MineBiome.Frost => For(Season.Winter, false),
        MineBiome.Magma => For(Season.Summer, true),
        MineBiome.Ruins => For(Season.Fall, true),
        _ => For(Season.Spring, false),
    };
}

/// <summary>
/// The audio files the client loads and the rules that pick which cue plays (docs/adr/ADR-039). Pure: the frame loop asks and queues the answer. The files are
/// made by <c>tools/synth_sounds.py</c> (the sounds and the rain loop) and <c>tools/synth_music.py</c> (the music); a unit test checks that the list here and the folder agree.
/// </summary>
public static class AudioCatalog
{
    /// <summary>The folder of the files, relative to the app root.</summary>
    public const string Folder = "audio/";

    /// <summary>Rain's volume, 0 to 100.</summary>
    public const int RainVolume = 55;

    /// <summary>A storm's rain volume, 0 to 100.</summary>
    public const int StormVolume = 85;

    /// <summary>Seconds between footsteps while walking.</summary>
    public const double StepIntervalSeconds = 0.34;

    /// <summary>A pickup within this many pixels of the farmer is the farmer's own.</summary>
    public const int OwnPickupRadius = 24;

    /// <summary>A neighbour's pickup within this many pixels is heard.</summary>
    public const int EarshotRadius = 200;

    private const int NeighbourPickupVolume = 40;

    /// <summary>First minute of the day counted as night (20:00) and the first one counted as morning (06:00), the same dusk and dawn the ambient tint uses.</summary>
    private const int NightFrom = 20 * 60;

    private const int NightUntil = 6 * 60;

    /// <summary>Gets every file the client loads: the sounds, the rain loop, then the eight music tracks (one per <see cref="MusicTrackIds.For"/>).</summary>
    public static IReadOnlyList<AudioFile> Files { get; } =
    [
        new(AudioKind.Sound, SoundIds.ToolHoe, "tool_hoe.wav", 0.1, -14.2, AudioMix.Action),
        new(AudioKind.Sound, SoundIds.ToolWater, "tool_water.wav", 0.1, -18.3, AudioMix.Action),
        new(AudioKind.Sound, SoundIds.ToolAxe, "tool_axe.wav", 0.1, -15.2, AudioMix.Action),
        new(AudioKind.Sound, SoundIds.ToolPickaxe, "tool_pickaxe.wav", 0.1, -18.2, AudioMix.Action),
        new(AudioKind.Sound, SoundIds.ToolScythe, "tool_scythe.wav", 0.1, -17.9, AudioMix.Action),
        new(AudioKind.Sound, SoundIds.Plant, "plant.wav", 0.1, -17.6, AudioMix.Action),
        new(AudioKind.Sound, SoundIds.Harvest, "harvest.wav", 0.1, -13.3, AudioMix.Action),
        new(AudioKind.Sound, SoundIds.Pickup, "pickup.wav", 0.06, -14.7, AudioMix.Action),
        new(AudioKind.Sound, SoundIds.Coin, "coin.wav", 0.12, -14.9, AudioMix.Action),
        new(AudioKind.Sound, SoundIds.Door, "door.wav", 0.5, -17.8, AudioMix.Action),
        new(AudioKind.Sound, SoundIds.UiClick, "ui_click.wav", 0, -18.2, AudioMix.Interface),
        new(AudioKind.Sound, SoundIds.UiOpen, "ui_open.wav", 0.12, -18.0, AudioMix.Interface),
        new(AudioKind.Sound, SoundIds.UiClose, "ui_close.wav", 0.12, -18.1, AudioMix.Interface),
        new(AudioKind.Sound, SoundIds.Footstep, "footstep.wav", 0.2, -21.8, AudioMix.Footstep),
        new(AudioKind.Sound, SoundIds.SwordSlash, "sword_slash.wav", 0.08, -16.0, AudioMix.Action),
        new(AudioKind.Sound, SoundIds.StaffCast, "staff_cast.wav", 0.1, -16.8, AudioMix.Action),
        new(AudioKind.Sound, SoundIds.MonsterHit, "monster_hit.wav", 0.1, -14.1, AudioMix.Action),
        new(AudioKind.Sound, SoundIds.MonsterDefeat, "monster_defeat.wav", 0.1, -14.3, AudioMix.Action),
        new(AudioKind.Sound, SoundIds.PotionDrink, "potion_drink.wav", 0.1, -14.4, AudioMix.Action),
        new(AudioKind.Sound, SoundIds.LevelUp, "level_up.wav", 0.2, -12.3, AudioMix.Stinger),
        new(AudioKind.Sound, SoundIds.ComboFinisher, "combo_finisher.wav", 0.25, -13.3, AudioMix.Stinger),
        new(AudioKind.Sound, SoundIds.Thunder, "thunder.wav", 1, -12.9, AudioMix.Stinger),
        new(AudioKind.Sound, SoundIds.Craft, "craft.wav", 0.15, -16.5, AudioMix.Stinger),
        new(AudioKind.Sound, SoundIds.Eat, "eat.wav", 0.2, -19.6, AudioMix.Action),
        new(AudioKind.Sound, SoundIds.Doorbell, "doorbell.wav", 1.5, -13.3, AudioMix.Stinger),
        new(AudioKind.Sound, SoundIds.MailChime, "mail_chime.wav", 1.5, -15.6, AudioMix.Stinger),
        new(AudioKind.Loop, LoopIds.Rain, "rain_loop.wav", 0, -15.5, AudioMix.Ambience),
        new(AudioKind.Track, MusicTrackIds.For(Season.Spring, false), "music_spring_day.wav", 0, -14.5, AudioMix.Music),
        new(AudioKind.Track, MusicTrackIds.For(Season.Spring, true), "music_spring_night.wav", 0, -15.4, AudioMix.Music),
        new(AudioKind.Track, MusicTrackIds.For(Season.Summer, false), "music_summer_day.wav", 0, -13.6, AudioMix.Music),
        new(AudioKind.Track, MusicTrackIds.For(Season.Summer, true), "music_summer_night.wav", 0, -16.1, AudioMix.Music),
        new(AudioKind.Track, MusicTrackIds.For(Season.Fall, false), "music_fall_day.wav", 0, -15.3, AudioMix.Music),
        new(AudioKind.Track, MusicTrackIds.For(Season.Fall, true), "music_fall_night.wav", 0, -15.7, AudioMix.Music),
        new(AudioKind.Track, MusicTrackIds.For(Season.Winter, false), "music_winter_day.wav", 0, -15.8, AudioMix.Music),
        new(AudioKind.Track, MusicTrackIds.For(Season.Winter, true), "music_winter_night.wav", 0, -15.3, AudioMix.Music),
    ];

    /// <summary>Returns the sound a tool makes when swung.</summary>
    /// <param name="tool">The tool in hand.</param>
    /// <returns>A sound id, or 0 for none (not a tool).</returns>
    public static int ToolSound(ToolKind tool) => tool switch
    {
        ToolKind.Hoe => SoundIds.ToolHoe,
        ToolKind.WateringCan => SoundIds.ToolWater,
        ToolKind.Axe => SoundIds.ToolAxe,
        ToolKind.Pickaxe => SoundIds.ToolPickaxe,
        ToolKind.Sword => SoundIds.SwordSlash,
        ToolKind.Staff => SoundIds.StaffCast,
        ToolKind.Scythe or ToolKind.MilkPail => SoundIds.ToolScythe,
        ToolKind.FishingRod => SoundIds.ToolWater,
        _ => 0,
    };

    /// <summary>Returns the sound for what the server answered to a use of an item. Swinging a tool already sounded when the key was pressed, so only the outcomes with a sound of their own answer.</summary>
    /// <param name="action">What happened.</param>
    /// <returns>A sound id, or 0 for none.</returns>
    public static int OutcomeSound(FarmAction action) => action switch
    {
        FarmAction.Harvested => SoundIds.Harvest,
        FarmAction.Planted or FarmAction.PlantedTree => SoundIds.Plant,
        FarmAction.Refilled => SoundIds.ToolWater,
        FarmAction.Descended => SoundIds.Door,
        _ => 0,
    };

    /// <summary>Returns whether a minute of the game day is night.</summary>
    /// <param name="minuteOfDay">Game minute of the day, 0 to 1439.</param>
    /// <returns><see langword="true"/> from 20:00 to 06:00.</returns>
    public static bool IsNight(int minuteOfDay) => minuteOfDay >= NightFrom || minuteOfDay < NightUntil;

    /// <summary>Returns how a pickup is heard: loud and centred when the local farmer picked it up, quiet and panned when a neighbour did, not at all when out of earshot.</summary>
    /// <param name="dx">Taker minus listener, map pixels along x.</param>
    /// <param name="dy">Taker minus listener, map pixels along y.</param>
    /// <param name="volume">The volume, 0 to 100.</param>
    /// <param name="pan">The stereo position, -100 to 100.</param>
    /// <returns><see langword="false"/> when the pickup is too far to hear.</returns>
    public static bool PickupCue(int dx, int dy, out int volume, out int pan)
    {
        long distance = ((long)dx * dx) + ((long)dy * dy);
        pan = 0;
        if (distance <= (long)OwnPickupRadius * OwnPickupRadius)
        {
            volume = 100;
            return true;
        }

        if (distance <= (long)EarshotRadius * EarshotRadius)
        {
            volume = NeighbourPickupVolume;
            pan = Math.Clamp(dx / 2, -100, 100);
            return true;
        }

        volume = 0;
        return false;
    }

    /// <summary>Returns the ambient loop for the weather: rain and storms are heard outdoors only, snow and fair weather are silent.</summary>
    /// <param name="weather">Today's weather.</param>
    /// <param name="indoors">Whether the player is on an indoor map.</param>
    /// <param name="volume">The loop's volume, 0 to 100.</param>
    /// <returns>A loop id, or <see cref="LoopIds.None"/>.</returns>
    public static int AmbientLoopFor(WeatherKind weather, bool indoors, out int volume)
    {
        volume = 0;
        if (indoors)
        {
            return LoopIds.None;
        }

        switch (weather)
        {
            case WeatherKind.Rain:
                volume = RainVolume;
                return LoopIds.Rain;
            case WeatherKind.Storm:
                volume = StormVolume;
                return LoopIds.Rain;
            default:
                return LoopIds.None;
        }
    }
}

/// <summary>Counts walking time into footsteps: one every <see cref="AudioCatalog.StepIntervalSeconds"/> while moving, the first one after half an interval.</summary>
public struct FootstepTimer
{
    private double _elapsed;

    /// <summary>Advances by one frame.</summary>
    /// <param name="moving">Whether the farmer is walking.</param>
    /// <param name="frameSeconds">Seconds since the previous frame.</param>
    /// <returns><see langword="true"/> when a footstep falls in this frame.</returns>
    public bool Advance(bool moving, double frameSeconds)
    {
        if (!moving)
        {
            _elapsed = 0;
            return false;
        }

        _elapsed += frameSeconds;
        double first = AudioCatalog.StepIntervalSeconds / 2;
        if (_elapsed < first)
        {
            return false;
        }

        _elapsed -= AudioCatalog.StepIntervalSeconds;
        return true;
    }
}

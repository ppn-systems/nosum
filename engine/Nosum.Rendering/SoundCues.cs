// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Rendering;

/// <summary>
/// The sounds, the music track and the ambient loop the game wants next, collected between frames and written into the frame's
/// <see cref="RenderCommandBuffer"/> by <see cref="Flush"/> (docs/adr/ADR-039): audio never costs an interop call of its own. Sounds are identified by number; this type
/// knows nothing about what they are. Nothing is allocated after construction.
/// </summary>
/// <remarks>
/// Not thread safe: the browser runs the frame and the callbacks that ask for sounds on one thread. A sound asked for again sooner than its minimum interval is dropped
/// (footsteps, repeated clicks), and at most <see cref="PendingCapacity"/> sounds wait for the next frame.
/// </remarks>
public sealed class SoundCues
{
    /// <summary>Highest sound id plus one: ids are 1 to <c>MaxSounds - 1</c>.</summary>
    public const int MaxSounds = 64;

    /// <summary>Most sounds that may wait for the next frame; further ones are dropped.</summary>
    public const int PendingCapacity = 16;

    private readonly int[] _ids = new int[PendingCapacity];
    private readonly int[] _volumes = new int[PendingCapacity];
    private readonly int[] _pans = new int[PendingCapacity];
    private readonly double[] _minInterval = new double[MaxSounds];
    private readonly double[] _lastPlayed = new double[MaxSounds];
    private int _pending;
    private int _music;
    private int _musicSent;
    private int _loop;
    private int _loopVolume;
    private int _loopSent;
    private int _loopVolumeSent;

    /// <summary>Initializes an empty queue: no sound is throttled until <see cref="SetMinInterval"/> says so.</summary>
    public SoundCues()
    {
        Array.Fill(_lastPlayed, double.NegativeInfinity);
    }

    /// <summary>Gets the number of sounds waiting for the next <see cref="Flush"/>.</summary>
    public int Pending => _pending;

    /// <summary>Gets the music track wanted, 0 for silence.</summary>
    public int Music => _music;

    /// <summary>Sets how soon a sound may play again after it was accepted.</summary>
    /// <param name="soundId">Sound id, 1 to <see cref="MaxSounds"/> - 1.</param>
    /// <param name="seconds">Minimum seconds between two plays; 0 for no limit.</param>
    public void SetMinInterval(int soundId, double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(soundId, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(soundId, MaxSounds);
        _minInterval[soundId] = Math.Max(0, seconds);
    }

    /// <summary>Asks for a sound to play once, at the next frame.</summary>
    /// <param name="soundId">Sound id, 1 to <see cref="MaxSounds"/> - 1.</param>
    /// <param name="nowSeconds">The scene clock, used only for the sound's minimum interval.</param>
    /// <param name="volume">Volume, 0 to 100.</param>
    /// <param name="pan">Stereo position, -100 to 100.</param>
    /// <returns><see langword="true"/> when the sound was queued; <see langword="false"/> when it was throttled, the queue was full or the id is invalid.</returns>
    public bool Play(int soundId, double nowSeconds, int volume = 100, int pan = 0)
    {
        if (soundId is < 1 or >= MaxSounds || _pending == PendingCapacity || nowSeconds - _lastPlayed[soundId] < _minInterval[soundId])
        {
            return false;
        }

        _lastPlayed[soundId] = nowSeconds;
        _ids[_pending] = soundId;
        _volumes[_pending] = volume;
        _pans[_pending] = pan;
        _pending++;
        return true;
    }

    /// <summary>Sets the music track; it is sent to the backend only when it changed since the last <see cref="Flush"/>.</summary>
    /// <param name="trackId">Track id, 0 for silence.</param>
    public void SetMusic(int trackId) => _music = Math.Max(trackId, 0);

    /// <summary>Sets the ambient loop; it is sent to the backend only when the loop or its volume changed since the last <see cref="Flush"/>.</summary>
    /// <param name="loopId">Loop id, 0 for none.</param>
    /// <param name="volume">Volume, 0 to 100.</param>
    public void SetAmbientLoop(int loopId, int volume)
    {
        _loop = Math.Max(loopId, 0);
        _loopVolume = _loop == 0 ? 0 : Math.Clamp(volume, 0, 100);
    }

    /// <summary>Writes the wanted changes and the waiting sounds into a frame that was just begun, then empties the queue.</summary>
    /// <param name="commands">The frame's buffer; call right after <see cref="RenderCommandBuffer.Begin"/>, so a full buffer never drops sound.</param>
    /// <returns>The number of commands written.</returns>
    public int Flush(RenderCommandBuffer commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        int before = commands.Count;
        if (_music != _musicSent)
        {
            _musicSent = _music;
            commands.SetMusic(_music);
        }

        if (_loop != _loopSent || _loopVolume != _loopVolumeSent)
        {
            _loopSent = _loop;
            _loopVolumeSent = _loopVolume;
            commands.SetAmbientLoop(_loop, _loopVolume);
        }

        for (int i = 0; i < _pending; i++)
        {
            commands.PlaySound(_ids[i], _volumes[i], _pans[i]);
        }

        _pending = 0;
        return commands.Count - before;
    }
}

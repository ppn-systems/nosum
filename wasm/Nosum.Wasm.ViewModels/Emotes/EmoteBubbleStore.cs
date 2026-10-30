// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using Nosum.Gameplay.Emotes;

namespace Nosum.Wasm.ViewModels.Emotes;

/// <summary>
/// The emote bubbles on screen (docs/adr/ADR-075 D1): the emote each farmer shows and until when, keyed by the player's map id. Socket callbacks
/// write it on any thread; the frame reads it through <see cref="TryGet"/>, which never allocates. Fixed capacity: when it is full the bubble that
/// expires first gives way.
/// </summary>
public sealed class EmoteBubbleStore
{
    /// <summary>How many bubbles are kept at once: one per farmer in sight, with room to spare.</summary>
    public const int Capacity = 64;

    private readonly Lock _gate = new();
    private readonly uint[] _ids = new uint[Capacity];
    private readonly byte[] _emotes = new byte[Capacity];
    private readonly long[] _untilMs = new long[Capacity];
    private int _count;

    /// <summary>Shows an emote above a farmer until <see cref="EmoteRules.ShownMs"/> after <paramref name="nowMs"/>; a newer emote replaces the one shown.</summary>
    /// <param name="playerId">The farmer's map id; zero (not joined) and unknown codes are ignored.</param>
    /// <param name="emote">The emote code.</param>
    /// <param name="nowMs">The current time, any monotonic origin shared with <see cref="TryGet"/>.</param>
    public void Show(uint playerId, byte emote, long nowMs)
    {
        if (playerId == 0 || !EmoteRules.IsValid(emote))
        {
            return;
        }

        lock (_gate)
        {
            int slot = this.Find(playerId);
            if (slot < 0)
            {
                slot = _count < Capacity ? _count++ : this.SoonestToExpire();
            }

            _ids[slot] = playerId;
            _emotes[slot] = emote;
            _untilMs[slot] = nowMs + EmoteRules.ShownMs;
        }
    }

    /// <summary>Looks up the emote a farmer shows now.</summary>
    /// <param name="playerId">The farmer's map id.</param>
    /// <param name="nowMs">The current time, the same origin as <see cref="Show"/>.</param>
    /// <param name="emote">The emote code when one is showing.</param>
    /// <returns><see langword="true"/> when the farmer has an emote showing at <paramref name="nowMs"/>.</returns>
    public bool TryGet(uint playerId, long nowMs, out byte emote)
    {
        lock (_gate)
        {
            int slot = this.Find(playerId);
            if (slot >= 0 && nowMs < _untilMs[slot])
            {
                emote = _emotes[slot];
                return true;
            }
        }

        emote = 0;
        return false;
    }

    // Called under the lock.
    private int Find(uint playerId)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_ids[i] == playerId)
            {
                return i;
            }
        }

        return -1;
    }

    // Called under the lock, when full: the slot whose bubble ends first.
    private int SoonestToExpire()
    {
        int soonest = 0;
        for (int i = 1; i < _count; i++)
        {
            if (_untilMs[i] < _untilMs[soonest])
            {
                soonest = i;
            }
        }

        return soonest;
    }
}

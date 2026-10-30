// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Nosum.Gameplay.Fishing;

namespace Nosum.Infrastructure.Fishing;

/// <summary>An open cast: the server's record of one line in the water (docs/adr/ADR-078 D4, D7). Never sent to the client.</summary>
/// <param name="Id">The cast id handed out; never zero.</param>
/// <param name="PlayerId">The player who cast.</param>
/// <param name="InstanceKey">The place the line was cast in (<see cref="Nosum.Gameplay.Maps.MapInstance.Key"/>).</param>
/// <param name="MapId">The map the line was cast in; names the water.</param>
/// <param name="CastAtMs">The server clock when the line was cast, in Unix milliseconds.</param>
/// <param name="BiteDelayMs">The bite wait drawn from the seed.</param>
/// <param name="WindowMs">The press window from the caster's fishing level.</param>
/// <param name="Seed">The seed the catch is rolled from.</param>
internal sealed record OpenCast(uint Id, Guid PlayerId, string InstanceKey, string MapId, long CastAtMs, int BiteDelayMs, int WindowMs, ulong Seed);

/// <summary>
/// The open casts, one per player, held in memory (docs/adr/ADR-078 D7). A restart loses them and nothing else: nothing is charged or added before the
/// reel-in. Guarded by one lock; the lock is never held across an await.
/// </summary>
internal sealed class FishingCasts
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, OpenCast> _open = [];
    private uint _last;

    /// <summary>Hands out the next cast id: never zero, never handed out twice in this process.</summary>
    /// <returns>The id.</returns>
    public uint NextId()
    {
        lock (_gate)
        {
            do
            {
                _last++;
            }
            while (_last == 0);

            return _last;
        }
    }

    /// <summary>Opens a cast; a player's previous open cast is replaced.</summary>
    /// <param name="cast">The cast.</param>
    public void Open(OpenCast cast)
    {
        lock (_gate)
        {
            _open[cast.PlayerId] = cast;
        }
    }

    /// <summary>Takes a player's open cast out of the store, if it is the cast with that id.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="castId">The cast id the player names.</param>
    /// <param name="cast">The cast taken out.</param>
    /// <returns>Whether the player had an open cast with that id.</returns>
    public bool TryTake(Guid playerId, uint castId, [NotNullWhen(true)] out OpenCast? cast)
    {
        lock (_gate)
        {
            if (_open.TryGetValue(playerId, out OpenCast? open) && open.Id == castId)
            {
                _ = _open.Remove(playerId);
                cast = open;
                return true;
            }
        }

        cast = null;
        return false;
    }

    /// <summary>Puts a taken cast back, unless the player has opened another one since (a newer cast always wins).</summary>
    /// <param name="cast">The cast to keep open.</param>
    public void Restore(OpenCast cast)
    {
        lock (_gate)
        {
            _ = _open.TryAdd(cast.PlayerId, cast);
        }
    }

    /// <summary>Drops every cast that has waited longer than <see cref="FishingRules.CastLifetimeMs"/>, so a player who left never leaves one behind.</summary>
    /// <param name="nowMs">The server clock, in Unix milliseconds.</param>
    public void Sweep(long nowMs)
    {
        lock (_gate)
        {
            List<Guid>? expired = null;
            foreach (KeyValuePair<Guid, OpenCast> entry in _open)
            {
                if (nowMs - entry.Value.CastAtMs > FishingRules.CastLifetimeMs)
                {
                    (expired ??= []).Add(entry.Key);
                }
            }

            if (expired is not null)
            {
                foreach (Guid playerId in expired)
                {
                    _ = _open.Remove(playerId);
                }
            }
        }
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.Maps;

namespace Nosum.Infrastructure.Presence;

/// <summary>
/// Per instance, chunk to the players whose interest set contains it (docs/adr/ADR-022 D4). Changed when a player enters or leaves an instance and when their
/// chunk changes, never per move; read by every publish. One short lock guards it: readers copy a chunk's subscribers out and send outside the lock.
/// </summary>
internal sealed class ChunkIndex
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Dictionary<ChunkCoord, List<PlayerPresence>>> _instances = new(StringComparer.Ordinal);

    /// <summary>Adds a player to the subscribers of every chunk of their interest set, when <paramref name="stillPresent"/> holds under the lock.</summary>
    public bool Add(PlayerPresence player, Func<bool> stillPresent)
    {
        ViewerState viewer = player.Viewer!;
        Span<ChunkCoord> chunks = stackalloc ChunkCoord[ChunkInterest.MaxChunks];
        int count = viewer.Interest.CopyTo(chunks);
        lock (_gate)
        {
            if (!stillPresent())
            {
                return false;
            }

            Dictionary<ChunkCoord, List<PlayerPresence>> instance = this.InstanceOf(player.Key);
            for (int i = 0; i < count; i++)
            {
                AddTo(instance, chunks[i], player);
            }

            return true;
        }
    }

    /// <summary>Removes a player from every chunk they were subscribed to.</summary>
    public void Remove(PlayerPresence player)
    {
        if (player.Viewer is not { } viewer)
        {
            return;
        }

        Span<ChunkCoord> chunks = stackalloc ChunkCoord[ChunkInterest.MaxChunks];
        lock (_gate)
        {
            // Copied under the lock: the snapshot loop may be moving the same interest set.
            int count = viewer.Interest.CopyTo(chunks);
            if (!_instances.TryGetValue(player.Key, out Dictionary<ChunkCoord, List<PlayerPresence>>? instance))
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                RemoveFrom(instance, chunks[i], player);
            }

            if (instance.Count == 0)
            {
                _ = _instances.Remove(player.Key);
            }
        }
    }

    /// <summary>Moves a player's interest set to a new chunk and updates the lists of the chunks that joined or left.</summary>
    public bool Move(PlayerPresence player, ChunkCoord destination, Span<ChunkCoord> entered, out int enteredCount, Span<ChunkCoord> left, out int leftCount)
    {
        ViewerState viewer = player.Viewer ?? throw new InvalidOperationException("The player is not subscribed.");
        lock (_gate)
        {
            if (!viewer.Interest.MoveTo(destination, entered, out enteredCount, left, out leftCount))
            {
                return false;
            }

            if (_instances.TryGetValue(player.Key, out Dictionary<ChunkCoord, List<PlayerPresence>>? instance))
            {
                for (int i = 0; i < leftCount; i++)
                {
                    RemoveFrom(instance, left[i], player);
                }

                for (int i = 0; i < enteredCount; i++)
                {
                    AddTo(instance, entered[i], player);
                }

                if (instance.Count == 0)
                {
                    _ = _instances.Remove(player.Key);
                }
            }

            return true;
        }
    }

    /// <summary>Appends the subscribers of a chunk.</summary>
    public void CopySubscribers(string instanceKey, ChunkCoord chunk, List<PlayerPresence> into)
    {
        lock (_gate)
        {
            if (_instances.TryGetValue(instanceKey, out Dictionary<ChunkCoord, List<PlayerPresence>>? instance)
                && instance.TryGetValue(chunk, out List<PlayerPresence>? subscribers))
            {
                into.AddRange(subscribers);
            }
        }
    }

    /// <summary>Tells whether a player's interest set contains a chunk.</summary>
    public bool IsInterested(PlayerPresence player, ChunkCoord chunk)
    {
        if (player.Viewer is not { } viewer)
        {
            return false;
        }

        lock (_gate)
        {
            return viewer.Interest.Contains(chunk);
        }
    }

    /// <summary>Tells whether the viewer's interest set contains the chunk another player stood in at the last tick.</summary>
    public bool Sees(PlayerPresence viewer, PlayerPresence other)
    {
        if (viewer.Viewer is not { } mine || other.Viewer is not { } theirs)
        {
            return ReferenceEquals(viewer, other);
        }

        lock (_gate)
        {
            return mine.Interest.Contains(theirs.Chunk);
        }
    }

    private Dictionary<ChunkCoord, List<PlayerPresence>> InstanceOf(string key)
    {
        if (!_instances.TryGetValue(key, out Dictionary<ChunkCoord, List<PlayerPresence>>? instance))
        {
            instance = [];
            _instances[key] = instance;
        }

        return instance;
    }

    private static void AddTo(Dictionary<ChunkCoord, List<PlayerPresence>> instance, ChunkCoord chunk, PlayerPresence player)
    {
        if (!instance.TryGetValue(chunk, out List<PlayerPresence>? list))
        {
            list = [];
            instance[chunk] = list;
        }

        list.Add(player);
    }

    private static void RemoveFrom(Dictionary<ChunkCoord, List<PlayerPresence>> instance, ChunkCoord chunk, PlayerPresence player)
    {
        if (instance.TryGetValue(chunk, out List<PlayerPresence>? list))
        {
            _ = list.Remove(player);
            if (list.Count == 0)
            {
                _ = instance.Remove(chunk);
            }
        }
    }
}

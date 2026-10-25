// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Maps;

namespace Nosum.Abstractions.Presence;

/// <summary>
/// What the server keeps about what one player can see (docs/adr/ADR-022): their chunk interest set, the other players they were last shown, and whether the chunk
/// state of the set was sent. Owned by the snapshot loop, which is the only thread that reads or writes it after the join; <see cref="IChunkMap"/> creates it.
/// </summary>
public sealed class ViewerState
{
    /// <summary>Initializes the state of a player who just entered an instance.</summary>
    /// <param name="interest">The player's interest set.</param>
    public ViewerState(ChunkInterest interest)
    {
        ArgumentNullException.ThrowIfNull(interest);

        this.Interest = interest;
        this.Chunk = interest.Center;
        Span<ChunkCoord> chunks = stackalloc ChunkCoord[ChunkInterest.MaxChunks];
        int count = interest.CopyTo(chunks);
        for (int i = 0; i < count; i++)
        {
            this.PendingEnter.Add(chunks[i]);
        }
    }

    /// <summary>Gets the chunks the player is interested in.</summary>
    public ChunkInterest Interest { get; }

    /// <summary>Gets or sets the chunk the player stood in at the last snapshot tick.</summary>
    public ChunkCoord Chunk { get; set; }

    /// <summary>Gets the chunks that joined the interest set whose state the client has not been sent yet: the whole set after a join, then the chunks each border crossing adds.</summary>
    public List<ChunkCoord> PendingEnter { get; } = [];

    /// <summary>Gets the other players the player was shown in the last snapshot: id to the tick stamp they were last seen at.</summary>
    public Dictionary<uint, int> Shown { get; } = [];

    /// <summary>Gets or sets the stamp of the current snapshot tick; <see cref="Shown"/> entries with another stamp were not in the last snapshot.</summary>
    public int Stamp { get; set; }
}

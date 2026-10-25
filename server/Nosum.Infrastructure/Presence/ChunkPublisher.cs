// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.Maps;

namespace Nosum.Infrastructure.Presence;

/// <summary>
/// <see cref="IChunkPublisher"/> over the chunk index and the presence: a chunk publish goes to the players whose interest set contains the chunk,
/// an instance publish to the instance's Nalix group (docs/adr/ADR-022 D4).
/// </summary>
public sealed class ChunkPublisher : IChunkPublisher
{
    private readonly IMapPresence _presence;
    private readonly IChunkMap _chunks;
    private readonly ConcurrentBag<List<PlayerPresence>> _lists = [];

    /// <summary>Initializes the publisher.</summary>
    /// <param name="presence">Sends to one connection and to an instance's group.</param>
    /// <param name="chunks">The chunk to subscribers index.</param>
    public ChunkPublisher(IMapPresence presence, IChunkMap chunks)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(chunks);

        _presence = presence;
        _chunks = chunks;
    }

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1031", Justification = "Best-effort fan-out: one dead connection must not cost the rest of the chunk its update.")]
    public async ValueTask PublishAsync(MapInstance instance, ChunkCoord chunk, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (!_lists.TryTake(out List<PlayerPresence>? subscribers))
        {
            subscribers = [];
        }

        try
        {
            _chunks.CopySubscribers(instance, chunk, subscribers);
            for (int i = 0; i < subscribers.Count; i++)
            {
                IConnection connection = subscribers[i].Connection;
                if (ReferenceEquals(connection, except) || connection.IsDisposed)
                {
                    continue;
                }

                try
                {
                    await _presence.SendAsync(connection, packet, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // A closing connection is cleaned up by its close event.
                }
            }
        }
        finally
        {
            subscribers.Clear();
            _lists.Add(subscribers);
        }
    }

    /// <inheritdoc/>
    public ValueTask PublishInstanceAsync(MapInstance instance, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default)
        => _presence.MulticastAsync(instance.Key, packet, except, cancellationToken);
}

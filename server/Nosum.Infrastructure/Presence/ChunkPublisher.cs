// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.Maps;

namespace Nosum.Infrastructure.Presence;

/// <summary>
/// <see cref="IChunkPublisher"/> over the chunk index and the presence: a chunk publish goes to the players whose interest set contains the chunk,
/// an instance publish to the instance's Nalix group (docs/adr/ADR-022 D4). Both sends go through <see cref="ViewerFanout"/>, so one stuck player
/// cannot hold a publish to the others (docs/adr/ADR-087, amendment 2026-11-02).
/// </summary>
public sealed class ChunkPublisher : IChunkPublisher
{
    private readonly IMapPresence _presence;
    private readonly IChunkMap _chunks;
    private readonly ViewerFanout _fanout;
    private readonly ConcurrentBag<List<PlayerPresence>> _lists = [];
    private readonly ConcurrentBag<List<IConnection>> _connections = [];

    /// <summary>Initializes the publisher.</summary>
    /// <param name="presence">Sends to one connection and to an instance's group.</param>
    /// <param name="chunks">The chunk to subscribers index.</param>
    public ChunkPublisher(IMapPresence presence, IChunkMap chunks)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(chunks);

        _presence = presence;
        _chunks = chunks;
        _fanout = new ViewerFanout(presence);
    }

    /// <inheritdoc/>
    public async ValueTask PublishAsync(MapInstance instance, ChunkCoord chunk, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        if (!_lists.TryTake(out List<PlayerPresence>? subscribers))
        {
            subscribers = [];
        }

        if (!_connections.TryTake(out List<IConnection>? recipients))
        {
            recipients = [];
        }

        try
        {
            _chunks.CopySubscribers(instance, chunk, subscribers);
            for (int i = 0; i < subscribers.Count; i++)
            {
                recipients.Add(subscribers[i].Connection);
            }

            await _fanout.SendAsync(recipients, packet, except, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            subscribers.Clear();
            _lists.Add(subscribers);
            recipients.Clear();
            _connections.Add(recipients);
        }
    }

    /// <inheritdoc/>
    public ValueTask PublishInstanceAsync(MapInstance instance, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default)
        => _presence.MulticastAsync(instance.Key, packet, except, cancellationToken);
}

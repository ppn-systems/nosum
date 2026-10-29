// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Look;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Look.S2C;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Gameplay.Maps;

namespace Nosum.Infrastructure.Players;

/// <summary>
/// Sends the one-off packets that keep a client's picture of the world in step with its chunk interest set (docs/adr/ADR-022 D3, D4, D6):
/// the full state of a chunk that joined the set, the notice that one left, a spawn (look ids and nickname) when another player comes into sight and a despawn when they go.
/// The snapshot loop decides when; this class only builds and sends.
/// </summary>
public sealed class ChunkStreamer
{
    private readonly IMapPresence _presence;
    private readonly IChunkPublisher _publisher;
    private readonly ILookService _looks;
    private readonly IChunkStateContributor[] _contributors;

    /// <summary>Initializes the streamer.</summary>
    /// <param name="presence">Sends to one connection.</param>
    /// <param name="publisher">Delivers a leave notice to the players who can see the leaver.</param>
    /// <param name="looks">Gives the look and nickname of a player who comes into sight.</param>
    /// <param name="contributors">The features that keep per-chunk world state (farm plots, placed objects, resource nodes, ground items).</param>
    public ChunkStreamer(IMapPresence presence, IChunkPublisher publisher, ILookService looks, IEnumerable<IChunkStateContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(looks);
        ArgumentNullException.ThrowIfNull(contributors);

        _presence = presence;
        _publisher = publisher;
        _looks = looks;
        _contributors = [.. contributors];
    }

    /// <summary>
    /// Sends the state of every chunk in <see cref="ViewerState.PendingEnter"/> and removes each from the list once sent; a chunk that fails stays for the next tick.
    /// A chunk with nothing in it is not sent.
    /// </summary>
    /// <param name="player">The player whose interest set grew.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the pending chunks were sent.</returns>
    public async ValueTask SendPendingStateAsync(PlayerPresence player, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(player);

        ViewerState viewer = player.Viewer!;
        while (viewer.PendingEnter.Count > 0)
        {
            ChunkCoord chunk = viewer.PendingEnter[^1];
            using PacketScope<ChunkState> lease = PacketFactory<ChunkState>.Acquire();
            ChunkState state = lease.Value;
            state.MapId = player.MapId;
            state.ChunkX = (ushort)chunk.X;
            state.ChunkY = (ushort)chunk.Y;
            state.ServerNowMs = 0;
            state.FarmTiles.Clear();
            state.Placed.Clear();
            state.Nodes.Clear();
            state.GroundItems.Clear();
            foreach (IChunkStateContributor contributor in _contributors)
            {
                await contributor.FillAsync(player.Instance, chunk, state, cancellationToken).ConfigureAwait(false);
            }

            if (!state.IsEmpty)
            {
                await _presence.SendAsync(player.Connection, state, cancellationToken).ConfigureAwait(false);
            }

            viewer.PendingEnter.RemoveAt(viewer.PendingEnter.Count - 1);
        }
    }

    /// <summary>Tells a player that chunks left their interest set, so the client drops its mirror of them.</summary>
    /// <param name="player">The player.</param>
    /// <param name="chunks">The chunks that left.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the notices were sent.</returns>
    public async ValueTask SendLeftAsync(PlayerPresence player, ReadOnlyMemory<ChunkCoord> chunks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(player);

        for (int i = 0; i < chunks.Length; i++)
        {
            ChunkCoord chunk = chunks.Span[i];
            using PacketScope<ChunkLeft> lease = PacketFactory<ChunkLeft>.Acquire();
            lease.Value.MapId = player.MapId;
            lease.Value.ChunkX = (ushort)chunk.X;
            lease.Value.ChunkY = (ushort)chunk.Y;
            await _presence.SendAsync(player.Connection, lease.Value, cancellationToken).ConfigureAwait(false);
            foreach (IChunkStateContributor contributor in _contributors)
            {
                await contributor.LeftAsync(player, chunk, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Shows a player to a viewer: sends the other's look ids and nickname, in full, as on a join.</summary>
    /// <param name="viewer">The player who now sees the other.</param>
    /// <param name="other">The player who came into sight.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the spawn was sent.</returns>
    public async ValueTask SendSpawnAsync(PlayerPresence viewer, PlayerPresence other, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(other);

        PlayerLookView view = await _looks.ViewAsync(other.PlayerId, other.AccountId, cancellationToken).ConfigureAwait(false);
        using PacketScope<LookChanged> lease = PacketFactory<LookChanged>.Acquire();
        // A pooled packet may come back with its nested row reset: always give it a fresh one.
        LookRow row = LookRow.Create();
        view.FillRow(row);
        lease.Value.Look = row;
        await _presence.SendAsync(viewer.Connection, lease.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tells a viewer that a player is no longer in sight (they walked away, changed map or left).</summary>
    /// <param name="viewer">The player who no longer sees the other.</param>
    /// <param name="playerId">The id of the player who went.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the despawn was sent.</returns>
    public async ValueTask SendDespawnAsync(PlayerPresence viewer, uint playerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        using PacketScope<PlayerLeft> lease = PacketFactory<PlayerLeft>.Acquire();
        lease.Value.PlayerId = playerId;
        await _presence.SendAsync(viewer.Connection, lease.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tells the players who can see a player that they left the instance.</summary>
    /// <param name="player">The presence of the player who left.</param>
    /// <param name="chunk">The chunk the player stood in.</param>
    /// <returns>A task that completes when the notice was published.</returns>
    public async ValueTask PublishLeftAsync(PlayerPresence player, ChunkCoord chunk)
    {
        ArgumentNullException.ThrowIfNull(player);

        using PacketScope<PlayerLeft> lease = PacketFactory<PlayerLeft>.Acquire();
        lease.Value.PlayerId = player.PlayerId;
        await _publisher.PublishAsync(player.Instance, chunk, lease.Value).ConfigureAwait(false);
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Runtime.Extensions;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;

namespace Nosum.Infrastructure.Presence;

/// <summary>
/// In-memory <see cref="IMapPresence"/>: one Nalix connection group per map instance (a shared map, or one player's private copy), and cleanup when a
/// connection closes.
/// </summary>
public sealed class MapPresence : IMapPresence
{
    private readonly IConnectionGroupRegistry _groups;
    private readonly ConcurrentDictionary<ulong, PlayerPresence> _byConnection = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<uint, PlayerPresence>> _byMap = new(StringComparer.Ordinal);
    private readonly EventHandler<IConnectionEventArgs> _onClosed;
    private int _nextPlayerId;

    /// <summary>Initializes the service.</summary>
    /// <param name="groups">Nalix group registry (<c>UseGroups()</c>) that holds the map groups.</param>
    public MapPresence(IConnectionGroupRegistry groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        _groups = groups;
        _onClosed = this.OnConnectionClosed;
    }

    /// <inheritdoc/>
    public event Func<PlayerPresence, ValueTask>? PlayerJoined;

    /// <inheritdoc/>
    public event Func<PlayerPresence, ValueTask>? PlayerLeft;

    /// <inheritdoc/>
    public int PlayerCount => _byConnection.Count;

    /// <inheritdoc/>
    public async ValueTask<PlayerPresence> JoinAsync(IConnection connection, Guid accountId, MapInstance instance, MotionState motion, uint? playerId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrEmpty(instance.MapId);

        await this.LeaveAsync(connection, cancellationToken).ConfigureAwait(false);

        uint id = playerId ?? (uint)Interlocked.Increment(ref _nextPlayerId);
        PlayerPresence player = new(id, accountId, instance, connection, motion);
        _byConnection[connection.ConnectionId] = player;
        _byMap.GetOrAdd(instance.Key, static _ => new ConcurrentDictionary<uint, PlayerPresence>())[id] = player;

        connection.ConnectionClosed += _onClosed;
        await _groups.AddToGroupAsync(MapGroup.Name(instance.Key), connection, cancellationToken).ConfigureAwait(false);

        if (connection.IsDisposed)
        {
            // Closed while joining: the close event may have fired before we subscribed.
            await this.LeaveAsync(connection, cancellationToken).ConfigureAwait(false);
            return player;
        }

        if (this.PlayerJoined is { } joined)
        {
            foreach (Func<PlayerPresence, ValueTask> handler in joined.GetInvocationList())
            {
                await handler(player).ConfigureAwait(false);
            }
        }

        return player;
    }

    /// <inheritdoc/>
    public async ValueTask LeaveAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_byConnection.TryRemove(connection.ConnectionId, out PlayerPresence? player))
        {
            return;
        }

        connection.ConnectionClosed -= _onClosed;
        if (_byMap.TryGetValue(player.Key, out ConcurrentDictionary<uint, PlayerPresence>? members))
        {
            _ = members.TryRemove(player.PlayerId, out _);
            if (members.IsEmpty)
            {
                _ = _byMap.TryRemove(new KeyValuePair<string, ConcurrentDictionary<uint, PlayerPresence>>(player.Key, members));
            }
        }

        await _groups.RemoveFromGroupAsync(MapGroup.Name(player.Key), connection, cancellationToken).ConfigureAwait(false);

        if (this.PlayerLeft is { } handlers)
        {
            foreach (Func<PlayerPresence, ValueTask> handler in handlers.GetInvocationList())
            {
                await handler(player).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public bool TryGet(IConnection connection, [NotNullWhen(true)] out PlayerPresence? player)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return _byConnection.TryGetValue(connection.ConnectionId, out player);
    }

    /// <inheritdoc/>
    public void CopyActiveMapIds(List<string> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        foreach (KeyValuePair<string, ConcurrentDictionary<uint, PlayerPresence>> map in _byMap)
        {
            if (!map.Value.IsEmpty)
            {
                into.Add(map.Key);
            }
        }
    }

    /// <inheritdoc/>
    public void CopyPlayers(string instanceKey, List<PlayerPresence> into)
    {
        ArgumentNullException.ThrowIfNull(instanceKey);
        ArgumentNullException.ThrowIfNull(into);

        if (_byMap.TryGetValue(instanceKey, out ConcurrentDictionary<uint, PlayerPresence>? members))
        {
            foreach (KeyValuePair<uint, PlayerPresence> member in members)
            {
                into.Add(member.Value);
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask SendAsync(IConnection connection, IPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(packet);

        await connection.SendAsync(packet, NetworkTransport.TCP, enableEncrypt: this.EncryptSends, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1031", Justification = "Best-effort fan-out: one dead connection must not cost the rest of the map its update.")]
    public async ValueTask MulticastAsync(string instanceKey, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        foreach (IConnection member in _groups.GetGroupMembers(MapGroup.Name(instanceKey)))
        {
            if (ReferenceEquals(member, except) || member.IsDisposed)
            {
                continue;
            }

            try
            {
                await member.SendAsync(packet, NetworkTransport.TCP, enableEncrypt: this.EncryptSends, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A closing connection is cleaned up by its close event.
            }
        }
    }

    /// <summary>
    /// Gets or sets whether pushes are encrypted. Encryption needs the secure handshake
    /// (<c>UseSecureConnections()</c>); the composition root turns it off only for plaintext development.
    /// </summary>
    public bool EncryptSends { get; set; } = true;

    [SuppressMessage("Design", "CA1031", Justification = "Raised from a connection close event; there is no caller to report to and cleanup is best effort.")]
    private void OnConnectionClosed(object? sender, IConnectionEventArgs e)
    {
        IConnection connection = e.Connection;
        _ = Task.Run(async () =>
        {
            try
            {
                await this.LeaveAsync(connection).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The player record is already removed before any step that can throw.
            }
        });
    }
}

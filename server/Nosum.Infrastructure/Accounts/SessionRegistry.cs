// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Security;
using Nalix.Codec.Pooling;
using Nalix.Runtime.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.S2C;

namespace Nosum.Infrastructure.Accounts;

/// <summary>In-memory <see cref="ISessionRegistry"/> for a single server process.</summary>
public sealed class SessionRegistry : ISessionRegistry
{
    private readonly IMapPresence _presence;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, IConnection> _active = [];
    private readonly Dictionary<ulong, Guid> _playerOf = [];

    // The ends still running, per player. An entry is added under _gate in the same step that takes the player off the active list, and it is removed
    // under _gate once the end has finished, so a sign-in never sees a player as gone while that player's end is still to run.
    private readonly ConcurrentDictionary<Guid, Task> _closing = new();
    private readonly EventHandler<IConnectionEventArgs> _onClosed;

    /// <summary>Initializes the registry.</summary>
    /// <param name="presence">Map presence: the older connection leaves its map (saving its place) before it is closed.</param>
    public SessionRegistry(IMapPresence presence)
    {
        ArgumentNullException.ThrowIfNull(presence);
        _presence = presence;
        _onClosed = (_, e) => this.Release(e.Connection);
    }

    /// <inheritdoc/>
    public event Func<Guid, ValueTask>? SessionEnded;

    /// <summary>Gets or sets whether the notice to the older connection is encrypted; off only for plaintext development.</summary>
    public bool EncryptSends { get; set; } = true;

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1031", Justification = "Ending the older connection is best effort: it may already be closing, and the new sign-in must still succeed.")]
    public async ValueTask ActivateAsync(Guid playerId, IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        IConnection? previous = null;
        Task? leaving = null;
        while (true)
        {
            Task? pending = null;
            lock (_gate)
            {
                // Every end of this player that is still running finishes first (its save, then its drop of the copy), so the sign-in reads after it.
                if (_closing.TryGetValue(playerId, out Task? running) && !running.IsCompleted)
                {
                    pending = running;
                }
                else
                {
                    _ = _active.TryGetValue(playerId, out previous);
                    if (previous is not null && previous.ConnectionId == connection.ConnectionId)
                    {
                        return;
                    }

                    // A connection that signs in as another player first leaves its earlier player.
                    if (_playerOf.TryGetValue(connection.ConnectionId, out Guid earlier) && earlier != playerId)
                    {
                        _ = _active.Remove(earlier);
                        leaving = this.BeginEnd(earlier, () => this.LeaveThenEndAsync(connection, earlier, cancellationToken));
                    }
                    else if (!_playerOf.ContainsKey(connection.ConnectionId))
                    {
                        connection.ConnectionClosed += _onClosed;
                    }

                    _active[playerId] = connection;
                    _playerOf[connection.ConnectionId] = playerId;
                    if (previous is not null)
                    {
                        _ = _playerOf.Remove(previous.ConnectionId);
                    }
                }
            }

            if (pending is null)
            {
                break;
            }

            await WaitQuietlyAsync(pending).ConfigureAwait(false);
        }

        if (leaving is not null)
        {
            await WaitQuietlyAsync(leaving).ConfigureAwait(false);
        }

        if (previous is null)
        {
            return;
        }

        await this.CloseAsync(previous, AccountStatus.SessionReplaced, "session replaced", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> EndAsync(Guid playerId, AccountStatus reason, CancellationToken cancellationToken = default)
    {
        Task end;
        lock (_gate)
        {
            if (!_active.TryGetValue(playerId, out IConnection? connection))
            {
                return false;
            }

            _ = _active.Remove(playerId);
            _ = _playerOf.Remove(connection.ConnectionId);
            end = this.BeginEnd(playerId, () => this.CloseThenEndAsync(connection, reason, playerId, cancellationToken));
        }

        await end.ConfigureAwait(false);
        return true;
    }

    // Out of the game first, so its position is saved once and a late move from it cannot overwrite a newer one; then told why; then closed.
    [SuppressMessage("Design", "CA1031", Justification = "Ending a connection is best effort: it may already be closing, and the caller must still go on.")]
    private async ValueTask CloseAsync(IConnection connection, AccountStatus reason, string disconnectReason, CancellationToken cancellationToken)
    {
        try
        {
            connection.Level = PermissionLevel.NONE;
            await _presence.LeaveAsync(connection, cancellationToken).ConfigureAwait(false);

            if (!connection.IsDisposed)
            {
                using PacketScope<SessionEnded> lease = PacketFactory<SessionEnded>.Acquire();
                lease.Value.Reason = reason;
                await connection.SendAsync(lease.Value, NetworkTransport.TCP, enableEncrypt: this.EncryptSends, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Fall through to the disconnect.
        }
        finally
        {
            connection.ConnectionClosed -= _onClosed;
            connection.Disconnect(disconnectReason);
        }
    }

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1031", Justification = "Release runs from logout or close event; cleanup is tracked in _closing and best-effort.")]
    public void Release(IConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        lock (_gate)
        {
            if (_playerOf.Remove(connection.ConnectionId, out Guid playerId)
                && _active.TryGetValue(playerId, out IConnection? current)
                && current.ConnectionId == connection.ConnectionId)
            {
                _ = _active.Remove(playerId);
                _ = this.BeginEnd(playerId, () => this.LeaveThenEndAsync(connection, playerId, CancellationToken.None));
            }
        }

        connection.ConnectionClosed -= _onClosed;
    }

    /// <inheritdoc/>
    public IConnection? GetActive(Guid playerId)
    {
        lock (_gate)
        {
            return _active.GetValueOrDefault(playerId);
        }
    }

    // Starts one player's end and records it as running. Called with _gate held, in the same step that took the player off the active list.
    private Task BeginEnd(Guid playerId, Func<Task> end)
    {
        Task running = Task.Run(end);
        _closing[playerId] = running;
        _ = running.ContinueWith(
            t =>
            {
                lock (_gate)
                {
                    _ = _closing.TryRemove(new KeyValuePair<Guid, Task>(playerId, t));
                }
            },
            TaskScheduler.Default);
        return running;
    }

    // The leave saves the place while the record is still registered; the ended handlers (which let the player's copies go) run only after the save.
    [SuppressMessage("Design", "CA1031", Justification = "Best effort leave before SessionEnded handlers; the handlers must still run.")]
    private async Task LeaveThenEndAsync(IConnection connection, Guid playerId, CancellationToken cancellationToken)
    {
        try
        {
            await _presence.LeaveAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort leave before SessionEnded handlers.
        }

        await this.InvokeEndedHandlersAsync(playerId).ConfigureAwait(false);
    }

    private async Task CloseThenEndAsync(IConnection connection, AccountStatus reason, Guid playerId, CancellationToken cancellationToken)
    {
        await this.CloseAsync(connection, reason, "session ended by the server", cancellationToken).ConfigureAwait(false);
        await this.InvokeEndedHandlersAsync(playerId).ConfigureAwait(false);
    }

    [SuppressMessage("Design", "CA1031", Justification = "A pending end's own failures are swallowed where it runs; waiting for it must not fail the sign-in.")]
    private static async Task WaitQuietlyAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort completion of prior session teardown.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Raised from a close; there is no caller to report to and the other handlers must still run.")]
    private async ValueTask InvokeEndedHandlersAsync(Guid playerId)
    {
        if (this.SessionEnded is not { } handlers)
        {
            return;
        }

        foreach (Func<Guid, ValueTask> handler in handlers.GetInvocationList())
        {
            try
            {
                await handler(playerId).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best effort.
            }
        }
    }
}

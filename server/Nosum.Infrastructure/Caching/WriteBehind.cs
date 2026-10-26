// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Data;

namespace Nosum.Infrastructure.Caching;

/// <summary>
/// Write-behind for the few writes whose loss cannot change money, items or progress (docs/adr/ADR-035 D7): the selected hotbar slot and the read flag of a mail.
/// A service keeps the new value in memory at once, stages the row write here, and the write happens when its window has passed, when the player leaves (a map change or a
/// disconnect), or at shutdown, in one unit of work per player. Staging the same key again replaces the write but keeps the window of the first change, so a player who clicks
/// all the time still costs one write per window. A crash loses at most what is staged: a hotbar slot reverts, a mail shows as unread again.
/// </summary>
public sealed class WriteBehind : IDisposable
{
    /// <summary>The window of the selected hotbar slot and of the read flag of a mail.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(5);

    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _flushing = new(1, 1);
    private readonly Dictionary<Guid, Dictionary<string, StagedWrite>> _pending = [];
    private readonly ITimer _timer;
    private DateTimeOffset? _timerDue;

    /// <summary>Initializes the queue.</summary>
    /// <param name="unitOfWork">Creates the unit of work of a flush.</param>
    /// <param name="time">The clock the windows run on; real time in the host.</param>
    public WriteBehind(IUnitOfWorkFactory unitOfWork, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);
        _unitOfWork = unitOfWork;
        _time = time;
        _timer = time.CreateTimer(static state => ((WriteBehind)state!).OnTimer(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Gets how many writes are waiting.</summary>
    public int Pending
    {
        get
        {
            lock (_gate)
            {
                return _pending.Values.Sum(p => p.Count);
            }
        }
    }

    /// <summary>Raised when a flush could not write something; the value is dropped (it is cosmetic by construction) and the host logs it.</summary>
    public event Action<Exception>? Failed;

    /// <summary>Stages a write for a player.</summary>
    /// <typeparam name="TValue">The value being written; kept so the owner can read it back until the write happened.</typeparam>
    /// <param name="playerId">The player the write belongs to.</param>
    /// <param name="key">Names what is written; staging the same key again replaces the earlier write.</param>
    /// <param name="value">The new value.</param>
    /// <param name="window">How long the write may wait, counted from the first change of this key.</param>
    /// <param name="write">Stages the row change in the unit of work of the flush.</param>
    public void Stage<TValue>(Guid playerId, string key, TValue value, TimeSpan window, Action<IUnitOfWork, TValue> write)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(write);

        DateTimeOffset now = _time.GetUtcNow();
        lock (_gate)
        {
            if (!_pending.TryGetValue(playerId, out Dictionary<string, StagedWrite>? items))
            {
                items = [];
                _pending[playerId] = items;
            }

            DateTimeOffset due = items.TryGetValue(key, out StagedWrite? earlier) ? earlier.Due : now + window;
            items[key] = new StagedWrite(value, due, unit => write(unit, value));
            if (_timerDue is null || due < _timerDue)
            {
                _timerDue = due;
                _ = _timer.Change(due > now ? due - now : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>Reads back a staged value that has not been written yet.</summary>
    /// <typeparam name="TValue">The type given to <see cref="Stage"/>.</typeparam>
    /// <param name="playerId">The player.</param>
    /// <param name="key">The key.</param>
    /// <param name="value">The value when one is waiting.</param>
    /// <returns>Whether a write is waiting.</returns>
    public bool TryPeek<TValue>(Guid playerId, string key, [MaybeNullWhen(false)] out TValue value)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(playerId, out Dictionary<string, StagedWrite>? items) && items.TryGetValue(key, out StagedWrite? found) && found.Value is TValue typed)
            {
                value = typed;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Tells whether anything is waiting for a player.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>Whether a write is waiting.</returns>
    public bool HasPending(Guid playerId)
    {
        lock (_gate)
        {
            return _pending.ContainsKey(playerId);
        }
    }

    /// <summary>Forgets a staged write (what it would write is gone, for example a deleted mail).</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="key">The key.</param>
    public void Discard(Guid playerId, string key)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(playerId, out Dictionary<string, StagedWrite>? items) && items.Remove(key) && items.Count == 0)
            {
                _ = _pending.Remove(playerId);
            }
        }
    }

    /// <summary>Writes everything waiting for one player now, in one unit of work (a map change, a disconnect).</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the writes were made.</returns>
    public Task FlushAsync(Guid playerId, CancellationToken cancellationToken = default)
        => this.FlushWhereAsync((id, _) => id == playerId, cancellationToken);

    /// <summary>Writes everything whose window has passed (what the timer does).</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the writes were made.</returns>
    public Task FlushDueAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        return this.FlushWhereAsync((_, staged) => staged.Due <= now, cancellationToken);
    }

    /// <summary>Writes everything waiting (shutdown).</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the writes were made.</returns>
    public Task FlushAllAsync(CancellationToken cancellationToken = default)
        => this.FlushWhereAsync((_, _) => true, cancellationToken);

    /// <inheritdoc/>
    public void Dispose()
    {
        _timer.Dispose();
        _flushing.Dispose();
    }

    [SuppressMessage("Design", "CA1031", Justification = "Raised by the timer; a failed flush is reported and must not stop the next one.")]
    private void OnTimer()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await this.FlushDueAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.Failed?.Invoke(ex);
            }
        });
    }

    [SuppressMessage("Design", "CA1031", Justification = "A write-behind row is cosmetic; one that cannot be written is reported and dropped, and the others still go.")]
    private async Task FlushWhereAsync(Func<Guid, StagedWrite, bool> chosen, CancellationToken cancellationToken)
    {
        await _flushing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<(Guid Player, List<(string Key, StagedWrite Item)> Items)> batches = [];
            lock (_gate)
            {
                foreach ((Guid player, Dictionary<string, StagedWrite> items) in _pending.ToList())
                {
                    List<(string Key, StagedWrite Item)> taken = [.. items.Where(i => chosen(player, i.Value)).Select(i => (i.Key, i.Value))];
                    if (taken.Count == 0)
                    {
                        continue;
                    }

                    foreach ((string key, _) in taken)
                    {
                        _ = items.Remove(key);
                    }

                    if (items.Count == 0)
                    {
                        _ = _pending.Remove(player);
                    }

                    batches.Add((player, taken));
                }
            }

            foreach ((Guid player, List<(string Key, StagedWrite Item)> items) in batches)
            {
                if (!await this.TryWriteAsync(items.Select(i => i.Item).ToList(), cancellationToken).ConfigureAwait(false))
                {
                    // One bad row (a mail deleted meanwhile) must not cost the others: they are written one by one.
                    foreach ((_, StagedWrite item) in items)
                    {
                        _ = await this.TryWriteAsync([item], cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            this.Reschedule();
            _ = _flushing.Release();
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "See above.")]
    private async Task<bool> TryWriteAsync(List<StagedWrite> items, CancellationToken cancellationToken)
    {
        try
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                foreach (StagedWrite item in items)
                {
                    item.Write(unit);
                }

                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (items.Count == 1)
            {
                this.Failed?.Invoke(ex);
            }

            return false;
        }
    }

    private void Reschedule()
    {
        lock (_gate)
        {
            DateTimeOffset? next = _pending.Values.SelectMany(p => p.Values).Select(p => (DateTimeOffset?)p.Due).Min();
            _timerDue = next;
            DateTimeOffset now = _time.GetUtcNow();
            _ = _timer.Change(next is { } due ? (due > now ? due - now : TimeSpan.Zero) : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private sealed record StagedWrite(object? Value, DateTimeOffset Due, Action<IUnitOfWork> Write);
}

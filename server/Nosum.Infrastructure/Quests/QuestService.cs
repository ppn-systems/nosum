// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Quests;
using Nosum.Contracts.Quest;
using Nosum.Contracts.Quest.S2C;
using Nosum.Domain.Quests;
using Nosum.Gameplay.Quests;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Quests;

/// <summary>
/// <see cref="IQuestService"/> on PostgreSQL. Every quest of the catalogue starts for every player
/// the first time their quests are read or something is reported (the catalogue is the tutorial chain, docs/adr/ADR-036). The counting
/// rules are <see cref="QuestDefinition"/>. A quest that pays coins is completed by one transfer that also marks the row completed, so a quest is
/// never completed without its reward and never rewarded twice.
/// <para>
/// Docs/adr/ADR-035: this class is the only writer of <c>player_quests</c>, so a player's quests are held in memory once they were read. Reporting an event that is not
/// the current step of any quest costs no command; a step that counts is written by key in the one transaction (or the reward transfer) and the copy follows after it
/// saved. Any failure drops the copy. The per-player lock covers the read, the change and the copy.
/// </para>
/// </summary>
public sealed class QuestService : IQuestService
{
    private const string RewardReason = "quest.reward";
    private const int MaxRewardAttempts = 3;

    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly IPlayerNotifier? _notifier;
    private readonly ITransferService? _transfers;
    private readonly StripedAsyncLock _locks = new();
    private readonly ConcurrentDictionary<Guid, Entry> _cache = new();
    private int _operations;

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">The clock for start and completion times; tests pass a fake one.</param>
    /// <param name="notifier">Pushes progress to the player; <see langword="null"/> to push nothing.</param>
    /// <param name="transfers">Pays the reward of a completed quest; <see langword="null"/> to pay nothing.</param>
    public QuestService(IUnitOfWorkFactory unitOfWork, TimeProvider time, IPlayerNotifier? notifier = null, ITransferService? transfers = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);

        _unitOfWork = unitOfWork;
        _time = time;
        _notifier = notifier;
        _transfers = transfers;
    }

    /// <inheritdoc/>
    public event Func<QuestCompletion, ValueTask>? Completed;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QuestView>> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        using (await _locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            Entry entry = await this.EntryAsync(playerId, cancellationToken).ConfigureAwait(false);
            return ViewsOf(entry.Quests);
        }
    }

    /// <summary>Lets go of a player's quests held in memory (they signed out); the next call reads the rows.</summary>
    /// <param name="playerId">The player.</param>
    public void Forget(Guid playerId) => _ = _cache.TryRemove(playerId, out _);

    /// <inheritdoc/>
    public async Task ReportAsync(Guid playerId, QuestEvent questEvent, CancellationToken cancellationToken = default)
    {
        List<QuestCompletion> completions = [];
        List<QuestView> updates = [];
        using (await _locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            try
            {
                Entry entry = await this.EntryAsync(playerId, cancellationToken).ConfigureAwait(false);
                List<(int QuestId, int[] Progress)> rewarded = [];
                List<QuestRecord> changed = [];
                foreach (QuestRecord quest in entry.Quests)
                {
                    if (quest.State != QuestState.Active || !QuestCatalog.TryGet(quest.QuestId, out QuestDefinition? definition))
                    {
                        continue;
                    }

                    int[] progress = (int[])quest.Progress.Clone();
                    if (!definition.Apply(progress, questEvent))
                    {
                        continue;
                    }

                    if (definition.IsComplete(progress) && definition.RewardCoins > 0 && _transfers is not null)
                    {
                        // Completed by the reward's own transaction below; this unit leaves the row alone.
                        rewarded.Add((quest.QuestId, progress));
                        continue;
                    }

                    QuestRecord next = quest with { Progress = progress };
                    if (definition.IsComplete(progress))
                    {
                        next = next with { State = QuestState.Completed, CompletedAt = _time.GetUtcNow() };
                        completions.Add(new QuestCompletion(playerId, quest.QuestId));
                    }

                    changed.Add(next);
                    updates.Add(ViewOf(next));
                }

                if (changed.Count > 0)
                {
                    IUnitOfWork unit = _unitOfWork.Create();
                    await using (unit.ConfigureAwait(false))
                    {
                        foreach (QuestRecord next in changed)
                        {
                            Stage(unit, playerId, next);
                        }

                        _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }

                    this.Store(playerId, entry, changed);
                }

                foreach ((int questId, int[] progress) in rewarded)
                {
                    QuestRecord done = await this.CompleteWithRewardAsync(playerId, entry, questId, progress, cancellationToken).ConfigureAwait(false);
                    this.Store(playerId, this.Current(playerId, entry), [done]);
                    updates.Add(ViewOf(done));
                    completions.Add(new QuestCompletion(playerId, questId));
                }
            }
            catch
            {
                // A failed save leaves the rows and the copy possibly different: read the rows again next time.
                this.Forget(playerId);
                throw;
            }
        }

        foreach (QuestView view in updates)
        {
            await this.PushAsync(playerId, view, cancellationToken).ConfigureAwait(false);
        }

        foreach (QuestCompletion completion in completions)
        {
            await this.RaiseCompletedAsync(completion).ConfigureAwait(false);
        }
    }

    // The coins and the completed row commit together (docs/adr/ADR-036 D2). A wallet that cannot take the coins completes the quest without them: the quest is still done.
    private async Task<QuestRecord> CompleteWithRewardAsync(Guid playerId, Entry entry, int questId, int[] progress, CancellationToken cancellationToken)
    {
        _ = QuestCatalog.TryGet(questId, out QuestDefinition? definition);
        QuestRecord started = entry.Quests.Single(q => q.QuestId == questId);
        QuestRecord done = started with { Progress = progress, State = QuestState.Completed, CompletedAt = _time.GetUtcNow() };
        TransferStatus status = TransferStatus.Conflict;
        for (int attempt = 0; attempt < MaxRewardAttempts && status == TransferStatus.Conflict; attempt++)
        {
            status = await _transfers!.ExecuteAsync(
                [new TransferLeg(TransferParty.Player(playerId), 0, 0, definition!.RewardCoins)],
                RewardReason,
                (inner, token) =>
                {
                    // The quest is active (checked under this player's lock, and nothing else writes quests): it is written by key, with no read.
                    Stage(inner, playerId, done);
                    return Task.FromResult(TransferStatus.Ok);
                },
                cancellationToken).ConfigureAwait(false);
        }

        if (status == TransferStatus.Ok)
        {
            return done;
        }

        // The wallet could not take the coins (or the transfer kept losing its race): the quest is still done, without them.
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            PlayerQuest? quest = await unit.Repository<PlayerQuest>().FirstOrDefaultAsync(q => q.PlayerId == playerId && q.QuestId == questId, cancellationToken).ConfigureAwait(false);
            if (quest is { State: QuestState.Active })
            {
                quest.Progress = progress;
                quest.State = QuestState.Completed;
                quest.CompletedAt = done.CompletedAt;
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return done;
    }

    // The row of a quest, written by key and only the columns a report changes.
    private static void Stage(IUnitOfWork unit, Guid playerId, QuestRecord quest)
        => unit.Repository<PlayerQuest>().Update(
            new PlayerQuest { PlayerId = playerId, QuestId = quest.QuestId, Progress = quest.Progress, State = quest.State, StartedAt = quest.StartedAt, CompletedAt = quest.CompletedAt },
            nameof(PlayerQuest.Progress),
            nameof(PlayerQuest.State),
            nameof(PlayerQuest.CompletedAt));

    private static List<QuestView> ViewsOf(IReadOnlyList<QuestRecord> quests)
    {
        List<QuestView> views = new(quests.Count);
        foreach (QuestRecord quest in quests)
        {
            views.Add(ViewOf(quest));
        }

        views.Sort(static (a, b) => a.QuestId.CompareTo(b.QuestId));
        return views;
    }

    private static QuestView ViewOf(QuestRecord quest) => new(quest.QuestId, quest.State, (int[])quest.Progress.Clone());

    // The player's quests: from memory, else from the rows, starting the catalogue quests the player does not have yet (written once, here).
    private async Task<Entry> EntryAsync(Guid playerId, CancellationToken cancellationToken)
    {
        this.Trim();
        if (_cache.TryGetValue(playerId, out Entry? known) && known.Quests.Count >= QuestCatalog.All.Count)
        {
            known.Touch();
            return known;
        }

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            IRepository<PlayerQuest> repository = unit.Repository<PlayerQuest>();
            List<PlayerQuest> quests = await repository.ListAsync(q => q.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
            foreach (QuestDefinition definition in QuestCatalog.All)
            {
                if (quests.Exists(q => q.QuestId == definition.Id))
                {
                    continue;
                }

                PlayerQuest started = new()
                {
                    PlayerId = playerId,
                    QuestId = definition.Id,
                    State = QuestState.Active,
                    Progress = new int[definition.ObjectiveCount],
                    StartedAt = _time.GetUtcNow(),
                };
                repository.Add(started);
                quests.Add(started);
            }

            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            Entry entry = new([.. quests.Select(q => new QuestRecord(q.QuestId, q.State, (int[])q.Progress.Clone(), q.StartedAt, q.CompletedAt))]);
            _cache[playerId] = entry;
            return entry;
        }
    }

    // The entry as it is now (a reward replaced it while the loop ran).
    private Entry Current(Guid playerId, Entry fallback) => _cache.TryGetValue(playerId, out Entry? now) ? now : fallback;

    // Replaces the changed records after their save succeeded; the lock of the player is held.
    private void Store(Guid playerId, Entry entry, List<QuestRecord> changed)
    {
        QuestRecord[] next = [.. entry.Quests.Select(q => changed.Find(c => c.QuestId == q.QuestId) ?? q)];
        _cache[playerId] = new Entry(next);
    }

    private void Trim()
    {
        if ((Interlocked.Increment(ref _operations) & 0xFF) != 0)
        {
            return;
        }

        long now = Environment.TickCount64;
        foreach (KeyValuePair<Guid, Entry> pair in _cache.Where(p => now - p.Value.LastUsed > IdleLimitMilliseconds).ToList())
        {
            _ = _cache.TryRemove(pair.Key, out _);
        }
    }

    private const long IdleLimitMilliseconds = 30 * 60 * 1000;

    private sealed record QuestRecord(int QuestId, QuestState State, int[] Progress, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt);

    private sealed class Entry
    {
        private long _lastUsed = Environment.TickCount64;

        public Entry(IReadOnlyList<QuestRecord> quests) => this.Quests = quests;

        public IReadOnlyList<QuestRecord> Quests { get; }

        public long LastUsed => Volatile.Read(ref _lastUsed);

        public void Touch() => Volatile.Write(ref _lastUsed, Environment.TickCount64);
    }

    private async Task PushAsync(Guid playerId, QuestView view, CancellationToken cancellationToken)
    {
        if (_notifier is null)
        {
            return;
        }

        using PacketScope<QuestUpdate> lease = PacketFactory<QuestUpdate>.Acquire();
        lease.Value.Quest = ToRow(view);
        _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
    }

    private static QuestRow ToRow(QuestView view)
    {
        QuestRow row = QuestRow.Create();
        row.QuestId = view.QuestId;
        row.State = view.State;
        foreach (int count in view.Progress)
        {
            row.Progress.Add(count);
        }

        return row;
    }

    [SuppressMessage("Design", "CA1031", Justification = "The quest is saved; a failing reward or unlock handler must not undo it or fail the action that reported progress.")]
    private async ValueTask RaiseCompletedAsync(QuestCompletion completion)
    {
        if (this.Completed is not { } handlers)
        {
            return;
        }

        foreach (Func<QuestCompletion, ValueTask> handler in handlers.GetInvocationList())
        {
            try
            {
                await handler(completion).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best effort, see above.
            }
        }
    }
}

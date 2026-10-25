// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Data;
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
/// the first time their quests are read or something is reported (the catalogue has only the tutorial
/// quest). The counting rules are <see cref="QuestDefinition"/>.
/// </summary>
public sealed class QuestService : IQuestService
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly TimeProvider _time;
    private readonly IPlayerNotifier? _notifier;
    private readonly StripedAsyncLock _locks = new();

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="time">The clock for start and completion times; tests pass a fake one.</param>
    /// <param name="notifier">Pushes progress to the player; <see langword="null"/> to push nothing.</param>
    public QuestService(IUnitOfWorkFactory unitOfWork, TimeProvider time, IPlayerNotifier? notifier = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(time);

        _unitOfWork = unitOfWork;
        _time = time;
        _notifier = notifier;
    }

    /// <inheritdoc/>
    public event Func<QuestCompletion, ValueTask>? Completed;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QuestView>> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        using (await _locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                List<PlayerQuest> quests = await this.LoadAsync(unit, playerId, cancellationToken).ConfigureAwait(false);
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return ViewsOf(quests);
            }
        }
    }

    /// <inheritdoc/>
    public async Task ReportAsync(Guid playerId, QuestEvent questEvent, CancellationToken cancellationToken = default)
    {
        List<QuestCompletion> completions = [];
        List<QuestView> updates = [];
        using (await _locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                List<PlayerQuest> quests = await this.LoadAsync(unit, playerId, cancellationToken).ConfigureAwait(false);
                foreach (PlayerQuest quest in quests)
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

                    // A new array, so the change is detected.
                    quest.Progress = progress;
                    if (definition.IsComplete(progress))
                    {
                        quest.State = QuestState.Completed;
                        quest.CompletedAt = _time.GetUtcNow();
                        completions.Add(new QuestCompletion(playerId, quest.QuestId));
                    }

                    updates.Add(ViewOf(quest));
                }

                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

    private static List<QuestView> ViewsOf(List<PlayerQuest> quests)
    {
        List<QuestView> views = new(quests.Count);
        foreach (PlayerQuest quest in quests)
        {
            views.Add(ViewOf(quest));
        }

        views.Sort(static (a, b) => a.QuestId.CompareTo(b.QuestId));
        return views;
    }

    private static QuestView ViewOf(PlayerQuest quest) => new(quest.QuestId, quest.State, (int[])quest.Progress.Clone());

    // Loads the player's quests and adds the catalogue quests the player does not have yet (not saved here).
    private async Task<List<PlayerQuest>> LoadAsync(IUnitOfWork unit, Guid playerId, CancellationToken cancellationToken)
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

        return quests;
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

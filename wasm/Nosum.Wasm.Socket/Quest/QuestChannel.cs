// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Quest;
using Nosum.Contracts.Quest.C2S;
using Nosum.Contracts.Quest.S2C;

namespace Nosum.Wasm.Socket.Quest;

/// <summary>One quest as the client mirrors it.</summary>
/// <param name="QuestId">Quest id from the quest catalogue.</param>
/// <param name="State">Active or completed.</param>
/// <param name="Progress">Count per objective, in the catalogue's objective order.</param>
public sealed record QuestInfo(int QuestId, QuestState State, IReadOnlyList<int> Progress);

/// <summary>
/// The client's mirror of the player's quests: loads the log and applies the server's pushed updates.
/// Progress is counted by the server when the player acts; the client only displays it.
/// </summary>
public sealed class QuestChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable _updates;
    private readonly Lock _gate = new();
    private readonly List<QuestInfo> _quests = [];

    /// <summary>Initializes the channel and starts listening for pushed updates.</summary>
    /// <param name="socket">The game connection.</param>
    public QuestChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
        _updates = socket.Subscribe<QuestUpdate>(this.OnUpdate);
    }

    /// <summary>Raised when the quests were loaded or one changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Copies the player's quests, ascending by quest id.</summary>
    /// <returns>A new list.</returns>
    public IReadOnlyList<QuestInfo> Quests()
    {
        lock (_gate)
        {
            return [.. _quests];
        }
    }

    /// <summary>Loads the quest log. Call it whenever the connection is signed in; a new player is given the tutorial quest.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <inheritdoc/>
    public void Dispose() => _updates.Dispose();

    private static QuestInfo InfoOf(QuestRow row) => new(row.QuestId, row.State, [.. row.Progress]);

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried at the next sign-in; there is no caller to report to.")]
    private async Task LoadAsync()
    {
        try
        {
            using PacketScope<GetQuests> request = PacketFactory<GetQuests>.Acquire();
            QuestLog log = await _socket.RequestAsync<QuestLog>(request.Value).ConfigureAwait(false);
            lock (_gate)
            {
                _quests.Clear();
                foreach (QuestRow row in log.Quests)
                {
                    _quests.Add(InfoOf(row));
                }

                _quests.Sort(static (a, b) => a.QuestId.CompareTo(b.QuestId));
            }

            this.Changed?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
    }

    private void OnUpdate(QuestUpdate update)
    {
        QuestInfo info = InfoOf(update.Quest);
        lock (_gate)
        {
            int at = _quests.FindIndex(q => q.QuestId == info.QuestId);
            if (at >= 0)
            {
                _quests[at] = info;
            }
            else
            {
                _quests.Add(info);
                _quests.Sort(static (a, b) => a.QuestId.CompareTo(b.QuestId));
            }
        }

        this.Changed?.Invoke();
    }
}

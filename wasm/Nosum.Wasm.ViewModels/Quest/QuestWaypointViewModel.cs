// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Quests;
using Nosum.Wasm.Socket.Quest;

namespace Nosum.Wasm.ViewModels.Quest;

/// <summary>
/// Where the active quest sends the farmer (docs/adr/ADR-093 D1): the waypoint the scene draws as an arrow at the screen edge and a dot on the minimap. It
/// follows the quest mirror, so the waypoint moves on as the quests do, and is empty when no active quest has a place.
/// </summary>
public sealed class QuestWaypointViewModel : IDisposable
{
    private readonly QuestChannel _channel;

    /// <summary>Initializes the view model and reads the quests already known.</summary>
    /// <param name="channel">The quest mirror.</param>
    public QuestWaypointViewModel(QuestChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        _channel = channel;
        _channel.Changed += this.OnChanged;
        this.Refresh();
    }

    /// <summary>Raised when the waypoint changes.</summary>
    public event Action? Changed;

    /// <summary>Gets the key of the active quest that has a waypoint, or <see langword="null"/> when there is none.</summary>
    public string? QuestKey { get; private set; }

    /// <summary>Gets the waypoint of the active quest, or <see langword="null"/> when there is none.</summary>
    public QuestWaypoint? Waypoint { get; private set; }

    /// <inheritdoc/>
    public void Dispose() => _channel.Changed -= this.OnChanged;

    private void OnChanged()
    {
        string? beforeKey = this.QuestKey;
        QuestWaypoint? beforePoint = this.Waypoint;
        this.Refresh();
        if (beforeKey != this.QuestKey || beforePoint != this.Waypoint)
        {
            this.Changed?.Invoke();
        }
    }

    private void Refresh()
    {
        QuestDefinition? active = ActiveQuest.Of(_channel.Quests());
        if (active is not null && QuestWaypoints.TryGetForQuest(active.Key, out QuestWaypoint? point))
        {
            this.QuestKey = active.Key;
            this.Waypoint = point;
        }
        else
        {
            this.QuestKey = null;
            this.Waypoint = null;
        }
    }
}

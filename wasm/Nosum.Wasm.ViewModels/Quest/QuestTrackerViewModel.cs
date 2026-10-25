// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Quest;
using Nosum.Gameplay.Quests;
using Nosum.Wasm.Socket.Quest;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Quest;

/// <summary>One objective line of the quest tracker.</summary>
/// <param name="Text">Localized objective, for example "Plant a seed".</param>
/// <param name="Progress">Localized count, for example "0/1".</param>
/// <param name="IsDone">Whether the objective reached its target.</param>
public sealed record ObjectiveLine(string Text, string Progress, bool IsDone);

/// <summary>
/// View model of the quest tracker: shows the first unfinished quest with its objectives, or the
/// banner of the quest that was just completed. Quest ids and counts come from the server; titles,
/// objectives and hints come from <c>Catalogue.resx</c> by the quest's stable key.
/// </summary>
public sealed class QuestTrackerViewModel : IObservableViewModel, IDisposable
{
    private readonly QuestChannel _channel;
    private readonly IStringLocalizer<Catalogue> _catalogue;
    private readonly IStringLocalizer<Language> _text;

    /// <summary>Initializes the view model and starts following the channel.</summary>
    /// <param name="channel">The quest mirror.</param>
    /// <param name="catalogue">Quest titles, objectives and hints.</param>
    /// <param name="text">UI text.</param>
    public QuestTrackerViewModel(QuestChannel channel, IStringLocalizer<Catalogue> catalogue, IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(text);

        _channel = channel;
        _catalogue = catalogue;
        _text = text;
        channel.Changed += this.OnChanged;
        this.Rebuild();
    }

    /// <summary>Raised when the tracker changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Gets whether there is anything to show.</summary>
    public bool IsVisible { get; private set; }

    /// <summary>Gets whether the shown quest is finished.</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>Gets the localized heading ("Quest").</summary>
    public string Heading => _text["Quest_Tracker_Heading"];

    /// <summary>Gets the localized quest title.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Gets the localized hint for the shown quest, or an empty string.</summary>
    public string Hint { get; private set; } = string.Empty;

    /// <summary>Gets the localized completion text while the shown quest is finished.</summary>
    public string CompletedText { get; private set; } = string.Empty;

    /// <summary>Gets the objective lines of the shown quest.</summary>
    public IReadOnlyList<ObjectiveLine> Objectives { get; private set; } = [];

    /// <inheritdoc/>
    public void Dispose() => _channel.Changed -= this.OnChanged;

    private void OnChanged()
    {
        this.Rebuild();
        this.Changed?.Invoke();
    }

    private void Rebuild()
    {
        // The first unfinished quest; when all are finished, the last finished one (its banner stays).
        QuestInfo? shown = null;
        foreach (QuestInfo quest in _channel.Quests())
        {
            if (!QuestCatalog.TryGet(quest.QuestId, out _))
            {
                continue;
            }

            shown = quest;
            if (quest.State == QuestState.Active)
            {
                break;
            }
        }

        if (shown is null || !QuestCatalog.TryGet(shown.QuestId, out QuestDefinition? definition))
        {
            this.IsVisible = false;
            this.Objectives = [];
            return;
        }

        string prefix = "Quest_" + definition.Key + "_";
        List<ObjectiveLine> lines = new(definition.ObjectiveCount);
        for (int i = 0; i < definition.ObjectiveCount; i++)
        {
            int target = definition.ObjectiveAt(i).Count;
            int done = i < shown.Progress.Count ? Math.Min(shown.Progress[i], target) : 0;
            lines.Add(new ObjectiveLine(
                _catalogue[prefix + "Objective_" + i.ToString(CultureInfo.InvariantCulture)],
                string.Format(CultureInfo.CurrentCulture, _text["Quest_Progress"].Value, done, target),
                done >= target));
        }

        this.IsVisible = true;
        this.IsCompleted = shown.State == QuestState.Completed;
        this.Title = _catalogue[prefix + "Title"];
        this.Hint = this.IsCompleted ? string.Empty : _catalogue[prefix + "Hint"].Value;
        this.CompletedText = this.IsCompleted ? string.Format(CultureInfo.CurrentCulture, _text["Quest_Banner"].Value, this.Title) : string.Empty;
        this.Objectives = lines;
    }
}

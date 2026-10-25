// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Building;
using Nosum.Wasm.Socket.Building;
using Nosum.Wasm.ViewModels.Feedback;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Building;

/// <summary>
/// The one-line messages of tending animals (docs/adr/ADR-028 D3): what the action key did inside a coop or a barn (fed the bench, petted an animal, collected an egg or milk) or why
/// it did nothing. Nothing is decided here; the text follows the server's answer and goes to the message line of the game.
/// </summary>
public sealed class AnimalCareViewModel : IDisposable
{
    private readonly BuildingChannel _buildings;
    private readonly GameMessagesViewModel _messages;
    private readonly ItemDescriber _describer;
    private readonly IStringLocalizer<Language> _text;

    /// <summary>Initializes the view model and subscribes to the building channel.</summary>
    /// <param name="buildings">Where the answers come from.</param>
    /// <param name="messages">The message line.</param>
    /// <param name="describer">Item names for the produce.</param>
    /// <param name="text">The texts.</param>
    public AnimalCareViewModel(BuildingChannel buildings, GameMessagesViewModel messages, ItemDescriber describer, IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(buildings);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(describer);
        ArgumentNullException.ThrowIfNull(text);

        _buildings = buildings;
        _messages = messages;
        _describer = describer;
        _text = text;
        buildings.Completed += this.OnCompleted;
    }

    /// <inheritdoc/>
    public void Dispose() => _buildings.Completed -= this.OnCompleted;

    private void OnCompleted(BuildingResultInfo result)
    {
        if (result.Action is not (BuildingAction.Feed or BuildingAction.Pet or BuildingAction.Collect))
        {
            return;
        }

        if (result.Status != BuildingStatus.Ok)
        {
            _messages.Notify(_text["Building_Status_" + result.Status].Value);
            return;
        }

        string text = result.Action switch
        {
            BuildingAction.Feed => _text["Building_Done_Feed"].Value,
            BuildingAction.Pet => string.Format(System.Globalization.CultureInfo.CurrentCulture, _text["Building_Done_Pet"].Value, _buildings.NameOf(result.AnimalId), result.Friendship),
            _ => string.Format(System.Globalization.CultureInfo.CurrentCulture, _text["Building_Done_Collect"].Value, _describer.NameOf(result.ItemId)),
        };
        _messages.Notify(text);
    }
}

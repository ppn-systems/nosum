// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Items;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Placement;

/// <summary>What the arrange panel asked the game to do; the frame loop takes them with <see cref="PlacementViewModel.TakeRequests"/>.</summary>
[Flags]
public enum PlacementRequests
{
    /// <summary>Nothing asked.</summary>
    None = 0,

    /// <summary>Turn the held or carried piece.</summary>
    Rotate = 1,

    /// <summary>Pick the piece in front up to move it, or put a carried piece back.</summary>
    Grab = 2,

    /// <summary>Take the piece in front back into the inventory.</summary>
    PickUp = 4,
}

/// <summary>
/// View model of the arrange-your-home panel, shown while the player stands in a place where furniture can be placed. The game loop
/// (which owns the target tile and the held item) reports what it is doing with <see cref="SetActive"/> and <see cref="SetCarrying"/>;
/// the panel's buttons ask for the same actions the keys do through <see cref="Request"/>. All text comes from the resources.
/// </summary>
public sealed class PlacementViewModel : IObservableViewModel
{
    private readonly IStringLocalizer<Catalogue> _catalogue;
    private readonly IStringLocalizer<Language> _text;
    private int _requests;
    private bool _active;
    private int _carryingItemId;

    /// <summary>Initializes the view model.</summary>
    /// <param name="catalogue">Item names.</param>
    /// <param name="text">UI text.</param>
    public PlacementViewModel(IStringLocalizer<Catalogue> catalogue, IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(text);

        _catalogue = catalogue;
        _text = text;
    }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>Gets whether the panel is shown (the player is in a place where furniture can be placed).</summary>
    public bool IsVisible => _active;

    /// <summary>Gets the localized panel title.</summary>
    public string Title => _text["Placement_Title"];

    /// <summary>Gets the localized explanation of the keys.</summary>
    public string Hint => _text["Placement_Hint"];

    /// <summary>Gets the localized label of the move button (grab, or put back while carrying).</summary>
    public string GrabLabel => _text[_carryingItemId == 0 ? "Placement_Move" : "Placement_Cancel"];

    /// <summary>Gets the one-word label of the place key (<c>E</c>) in the key strip.</summary>
    public string StripPlaceLabel => _text["Placement_Strip_Place"];

    /// <summary>Gets the one-word label of the rotate key (<c>R</c>) in the key strip.</summary>
    public string StripRotateLabel => _text["Placement_Strip_Rotate"];

    /// <summary>Gets the one-word label of the move key (<c>F</c>) in the key strip: "Cancel" while a piece is carried.</summary>
    public string StripMoveLabel => _text[_carryingItemId == 0 ? "Placement_Strip_Move" : "Placement_Strip_Cancel"];

    /// <summary>Gets the one-word label of the stow key (<c>Q</c>) in the key strip.</summary>
    public string StripStowLabel => _text["Placement_Strip_Stow"];

    /// <summary>Gets the localized "carrying: item" line, or <see langword="null"/> when nothing is carried.</summary>
    public string? CarryingText
        => _carryingItemId != 0 && ItemCatalog.TryGet(_carryingItemId, out ItemDefinition? item)
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, _text["Placement_Carrying"].Value, _catalogue["Item_" + item.Key + "_Name"].Value)
            : null;

    /// <summary>Asks the game to do something, as a key press would. May be called from any thread.</summary>
    /// <param name="request">What to do.</param>
    public void Request(PlacementRequests request) => _ = Interlocked.Or(ref _requests, (int)request);

    /// <summary>Takes the requests made since the last call; called by the frame loop, allocation-free.</summary>
    /// <returns>The requests, or <see cref="PlacementRequests.None"/>.</returns>
    public PlacementRequests TakeRequests() => (PlacementRequests)Interlocked.Exchange(ref _requests, 0);

    /// <summary>Shows or hides the panel; the game loop calls it when the map changes.</summary>
    /// <param name="active">Whether the player is in a place where furniture can be placed.</param>
    public void SetActive(bool active)
    {
        if (_active != active)
        {
            _active = active;
            this.Changed?.Invoke();
        }
    }

    /// <summary>Reports the piece being carried to a new spot, or none.</summary>
    /// <param name="itemId">The item the carried piece is made from, or zero.</param>
    public void SetCarrying(int itemId)
    {
        if (_carryingItemId != itemId)
        {
            _carryingItemId = itemId;
            this.Changed?.Invoke();
        }
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Placeable;
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

    /// <summary>Puts a carried piece back where it was (the panel's close button and Escape); nothing when no piece is carried.</summary>
    Cancel = 8,

    /// <summary>Place the held piece (or set down the carried one) on the ghost, as the action key does; the touch "Place" button asks for it.</summary>
    Place = 16,
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
    private bool _ghostShown;
    private PlaceStatus _ghostStatus = PlaceStatus.Ok;
    private bool _confirmingBuild;
    private bool _stowMissed;

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

    /// <summary>Gets whether the ghost of the held or carried piece is shown on the map.</summary>
    public bool GhostShown => _ghostShown;

    /// <summary>Gets the reason the ghost cannot be placed where it is, or <see cref="PlaceStatus.Ok"/> when it can (or no ghost is shown).</summary>
    public PlaceStatus GhostStatus => _ghostStatus;

    /// <summary>Gets whether the next place press will build a farm building (docs/adr/ADR-028): it needs a second press to confirm.</summary>
    public bool ConfirmingBuild => _confirmingBuild;

    /// <summary>
    /// Gets the one line the panel shows under the key strip: "nothing to stow" after a stow press with no piece in front, else the confirmation
    /// for a building, else the reason the ghost is invalid, else nothing. Every line is a resource string, so it follows the language.
    /// </summary>
    public string? NoticeText
    {
        get
        {
            if (_stowMissed)
            {
                return _text["Placement_NothingToStow"].Value;
            }

            if (!_ghostShown)
            {
                return null;
            }

            if (_confirmingBuild)
            {
                return _text["Placement_ConfirmBuild"].Value;
            }

            return _ghostStatus == PlaceStatus.Ok ? null : _text["Place_Status_" + _ghostStatus].Value;
        }
    }

    /// <summary>Gets whether <see cref="NoticeText"/> is a warning (the ghost is invalid) rather than a confirmation request or a note.</summary>
    public bool NoticeIsError => !_stowMissed && !_confirmingBuild && _ghostStatus != PlaceStatus.Ok;

    /// <summary>Gets whether the strip is showing "nothing in front to stow" (<see cref="NoticeText"/>); the game loop clears it after a moment.</summary>
    public bool StowMissed => _stowMissed;

    /// <summary>Shows or clears the "nothing in front to stow" line; the game loop calls it each frame with the time left on the note.</summary>
    /// <param name="shown">Whether the line is shown.</param>
    public void SetStowMissed(bool shown)
    {
        if (_stowMissed != shown)
        {
            _stowMissed = shown;
            this.Changed?.Invoke();
        }
    }

    /// <summary>Asks the game to do something, as a key press would. May be called from any thread.</summary>
    /// <param name="request">What to do.</param>
    public void Request(PlacementRequests request) => _ = Interlocked.Or(ref _requests, (int)request);

    /// <summary>Takes the requests made since the last call; called by the frame loop, allocation-free.</summary>
    /// <returns>The requests, or <see cref="PlacementRequests.None"/>.</returns>
    public PlacementRequests TakeRequests() => (PlacementRequests)Interlocked.Exchange(ref _requests, 0);

    /// <summary>Whether the player closed the arrange panel with its close button or Escape (<see cref="Dismiss"/>); cleared by <see cref="ClearDismissed"/>.</summary>
    public bool Dismissed { get; private set; }

    /// <summary>Closes arrange mode from the panel: the carried piece goes back where it was, and the panel stays closed until the player picks a
    /// piece again or holds another item (<see cref="ClearDismissed"/>).</summary>
    public void Dismiss()
    {
        this.Dismissed = true;
        this.Request(PlacementRequests.Cancel);
        if (_active)
        {
            _active = false;
            this.Changed?.Invoke();
        }
    }

    /// <summary>Lets the arrange panel show again: the player picked a piece or held another item.</summary>
    public void ClearDismissed() => this.Dismissed = false;

    /// <summary>Shows or hides the panel; the game loop calls it when the map changes.</summary>
    /// <param name="active">Whether the player is in a place where furniture can be placed.</param>
    public void SetActive(bool active)
    {
        active &= !this.Dismissed;
        if (_active != active)
        {
            _active = active;
            this.Changed?.Invoke();
        }
    }

    /// <summary>Reports the ghost each frame: whether it is shown, why it cannot be placed, and whether a building press is waiting for confirmation.</summary>
    /// <param name="shown">Whether the ghost is drawn.</param>
    /// <param name="status">Why the ghost is invalid, or <see cref="PlaceStatus.Ok"/>.</param>
    /// <param name="confirmingBuild">Whether the next place press builds (a second press is pending).</param>
    public void SetGhost(bool shown, PlaceStatus status, bool confirmingBuild)
    {
        if (!shown)
        {
            status = PlaceStatus.Ok;
            confirmingBuild = false;
        }

        if (_ghostShown == shown && _ghostStatus == status && _confirmingBuild == confirmingBuild)
        {
            return;
        }

        _ghostShown = shown;
        _ghostStatus = status;
        _confirmingBuild = confirmingBuild;
        this.Changed?.Invoke();
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

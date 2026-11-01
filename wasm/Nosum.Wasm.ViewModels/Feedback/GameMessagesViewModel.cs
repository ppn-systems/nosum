// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Market;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Player;
using Nosum.Gameplay.Skills;
using Nosum.Wasm.Socket.Combat;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.Socket.Placeable;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Feedback;

/// <summary>One toast on the message line: an id so the view can key it, and its localized text.</summary>
/// <param name="Id">A number unique for this view model, in the order the toasts were shown.</param>
/// <param name="Text">The localized text.</param>
/// <param name="Count">How many times the same text was shown while it was on the line (at least 1); shown as "×N" when above 1.</param>
/// <param name="Life">Changes each time the toast is shown again, so the timer of an earlier showing leaves the toast alone.</param>
public sealed record GameToast(long Id, string Text, int Count = 1, long Life = 0);

/// <summary>
/// The toast line above the hotbar: why the server refused an action ("Till the soil first", "Something is in the way"), shown for a moment.
/// The server sends status codes; the text comes from <c>Language.resx</c>. Up to <see cref="MaxToasts"/> toasts are stacked, the newest last;
/// a new one pushes out the oldest, and each goes away after <see cref="MessageDuration"/> (docs/design/ui-standard.md section 5).
/// A toast whose text is already on the line is not added again: its count goes up and its timer starts over.
/// </summary>
public sealed class GameMessagesViewModel : IObservableViewModel, IDisposable
{
    /// <summary>How long one toast stays on the line before it fades out and goes (docs/design/ui-standard.md section 5).</summary>
    public static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(3);

    /// <summary>How many toasts the line shows at once.</summary>
    public const int MaxToasts = 2;

    private readonly FarmChannel _farm;
    private readonly PlaceableChannel _placeables;
    private readonly InventoryViewModel _inventory;
    private readonly IStringLocalizer<Language> _text;
    private readonly MarketChannel? _market;
    private readonly ProgressChannel? _progress;
    private readonly CombatChannel? _combat;
    private readonly List<GameToast> _toasts = new(MaxToasts);
    private long _nextId;
    private long _nextLife;

    /// <summary>Initializes the view model and starts following the channels.</summary>
    /// <param name="farm">The farm channel (answers to item use).</param>
    /// <param name="placeables">The placeable channel (answers to placing furniture).</param>
    /// <param name="inventory">The inventory view model (refused moves).</param>
    /// <param name="text">UI text.</param>
    public GameMessagesViewModel(FarmChannel farm, PlaceableChannel placeables, InventoryViewModel inventory, IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(farm);
        ArgumentNullException.ThrowIfNull(placeables);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(text);

        _farm = farm;
        _placeables = placeables;
        _inventory = inventory;
        _text = text;
        farm.UseCompleted += this.OnUseCompleted;
        placeables.Completed += this.OnPlaceCompleted;
        placeables.PickUpCompleted += this.OnPickUpCompleted;
        inventory.Refused += this.Show;
    }

    /// <summary>Initializes the view model and also reports what the shipping bin paid (or why it paid nothing).</summary>
    /// <param name="farm">The farm channel (answers to item use).</param>
    /// <param name="placeables">The placeable channel (answers to placing furniture).</param>
    /// <param name="inventory">The inventory view model (refused moves).</param>
    /// <param name="text">UI text.</param>
    /// <param name="market">The market channel (answers to shipping).</param>
    public GameMessagesViewModel(FarmChannel farm, PlaceableChannel placeables, InventoryViewModel inventory, IStringLocalizer<Language> text, MarketChannel market)
        : this(farm, placeables, inventory, text)
    {
        ArgumentNullException.ThrowIfNull(market);
        _market = market;
        market.Completed += this.OnMarketCompleted;
    }

    /// <summary>Initializes the view model and also tells the player when a skill levels up and why eating was refused (docs/adr/ADR-023).</summary>
    /// <param name="farm">The farm channel (answers to item use).</param>
    /// <param name="placeables">The placeable channel (answers to placing furniture).</param>
    /// <param name="inventory">The inventory view model (refused moves).</param>
    /// <param name="text">UI text.</param>
    /// <param name="market">The market channel (answers to shipping).</param>
    /// <param name="progress">The energy and skills channel.</param>
    public GameMessagesViewModel(FarmChannel farm, PlaceableChannel placeables, InventoryViewModel inventory, IStringLocalizer<Language> text, MarketChannel market, ProgressChannel progress)
        : this(farm, placeables, inventory, text, market)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _progress = progress;
        progress.LevelledUp += this.OnLevelUp;
        progress.EatCompleted += this.OnEatCompleted;
    }

    /// <summary>Initializes the view model and also follows combat level-ups.</summary>
    /// <param name="farm">The farm channel (answers to item use).</param>
    /// <param name="placeables">The placeable channel (answers to placing furniture).</param>
    /// <param name="inventory">The inventory view model (refused moves).</param>
    /// <param name="text">UI text.</param>
    /// <param name="market">The market channel (answers to shipping).</param>
    /// <param name="progress">The energy and skills channel.</param>
    /// <param name="combat">The combat channel.</param>
    public GameMessagesViewModel(FarmChannel farm, PlaceableChannel placeables, InventoryViewModel inventory, IStringLocalizer<Language> text, MarketChannel market, ProgressChannel progress, CombatChannel combat)
        : this(farm, placeables, inventory, text, market, progress)
    {
        ArgumentNullException.ThrowIfNull(combat);
        _combat = combat;
        combat.LevelledUp += this.OnCombatLevelUp;
    }

    /// <summary>Raised when <see cref="Message"/> changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Gets the toasts on the line, oldest first: at most <see cref="MaxToasts"/>, none when the line is empty.</summary>
    public IReadOnlyList<GameToast> Toasts => _toasts.ToArray();

    /// <summary>Gets the newest message, or <see langword="null"/> when the line is empty.</summary>
    public string? Message => _toasts.Count > 0 ? _toasts[^1].Text : null;

    /// <summary>Maps a status code to its localized text.</summary>
    /// <param name="status">The server's outcome code.</param>
    /// <returns>The text, or <see langword="null"/> for <see cref="FarmStatus.Ok"/>.</returns>
    public string? TextOf(FarmStatus status) => status == FarmStatus.Ok ? null : _text["Farm_Status_" + status].Value;

    /// <summary>Maps a placeable status code to its localized text.</summary>
    /// <param name="status">The server's outcome code.</param>
    /// <returns>The text, or <see langword="null"/> for <see cref="PlaceStatus.Ok"/>.</returns>
    public string? TextOf(PlaceStatus status) => status == PlaceStatus.Ok ? null : _text["Place_Status_" + status].Value;

    /// <inheritdoc/>
    public void Dispose()
    {
        _farm.UseCompleted -= this.OnUseCompleted;
        _placeables.Completed -= this.OnPlaceCompleted;
        _placeables.PickUpCompleted -= this.OnPickUpCompleted;
        _inventory.Refused -= this.Show;
        if (_market is not null)
        {
            _market.Completed -= this.OnMarketCompleted;
        }

        if (_progress is not null)
        {
            _progress.LevelledUp -= this.OnLevelUp;
            _progress.EatCompleted -= this.OnEatCompleted;
        }

        if (_combat is not null)
        {
            _combat.LevelledUp -= this.OnCombatLevelUp;
        }
    }

    private void OnCombatLevelUp(int level)
    {
        if (_combat is { IsCombatSkillLevelUp: false })
        {
            return;
        }

        this.OnLevelUp((byte)SkillKind.Combat, level);
    }

    private void OnLevelUp(byte skill, int level)
        => this.Show(string.Format(System.Globalization.CultureInfo.CurrentCulture, _text["Skill_LevelUp"].Value, _text["Skill_" + (SkillKind)skill].Value, level));

    private void OnEatCompleted(EatStatus status)
    {
        if (status != EatStatus.Ok)
        {
            this.Show(_text["Eat_Status_" + status].Value);
        }
    }

    private void OnMarketCompleted(MarketAction action, MarketStatus status, long earned)
    {
        if (action != MarketAction.Ship)
        {
            return;
        }

        this.Show(status == MarketStatus.Ok
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, _text["Market_Shipped"].Value, earned)
            : _text["Market_Status_" + status].Value);
    }

    private void OnUseCompleted(FarmStatus status, FarmAction action, int worked)
    {
        _ = action;
        _ = worked;

        // A swing that came too soon is dropped without a word: the key was just held (docs/adr/ADR-021 D8).
        if (status != FarmStatus.Cooldown && this.TextOf(status) is { } text)
        {
            this.Show(text);
        }
    }

    private void OnPlaceCompleted(PlaceStatus status)
    {
        if (this.TextOf(status) is { } text)
        {
            this.Show(text);
        }
    }

    // A piece taken back into the bag is confirmed, with the way back: nothing is spent, so the piece can be placed again.
    private void OnPickUpCompleted(PlaceStatus status)
    {
        if (status == PlaceStatus.Ok)
        {
            this.Show(_text["Place_Stowed"].Value);
        }
    }

    /// <summary>Shows a line on the message line (a refusal from a page that is not open, such as a chest the player may not open).</summary>
    /// <param name="text">The localized text.</param>
    public void Notify(string text) => this.Show(text);

    private void Show(string text)
    {
        // The same text already on the line: count it again in place (same spot, same id) and start its timer over.
        int existing = _toasts.FindIndex(t => t.Text == text);
        if (existing >= 0)
        {
            GameToast again = _toasts[existing] with { Count = _toasts[existing].Count + 1, Life = ++_nextLife };
            _toasts[existing] = again;
            this.Changed?.Invoke();
            _ = this.RemoveLaterAsync(again.Id, again.Life);
            return;
        }

        GameToast toast = new(++_nextId, text, 1, ++_nextLife);
        _toasts.Add(toast);
        if (_toasts.Count > MaxToasts)
        {
            _toasts.RemoveAt(0);
        }

        this.Changed?.Invoke();
        _ = this.RemoveLaterAsync(toast.Id, toast.Life);
    }

    private async Task RemoveLaterAsync(long id, long life)
    {
        await Task.Delay(MessageDuration).ConfigureAwait(false);

        // A toast pushed out by a newer one has already gone. A toast that was shown again has a newer life and stays until its own timer ends.
        int index = _toasts.FindIndex(t => t.Id == id);
        if (index >= 0 && _toasts[index].Life == life)
        {
            _toasts.RemoveAt(index);
            this.Changed?.Invoke();
        }
    }
}

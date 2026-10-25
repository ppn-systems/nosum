// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Market;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Feedback;

/// <summary>
/// The short message line above the hotbar: why the server refused an action ("Till the soil first"),
/// shown for a moment. The server sends status codes; the text comes from <c>Language.resx</c>.
/// </summary>
public sealed class GameMessagesViewModel : IObservableViewModel, IDisposable
{
    /// <summary>How long a message stays.</summary>
    public static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(2.5);

    private readonly FarmChannel _farm;
    private readonly InventoryViewModel _inventory;
    private readonly IStringLocalizer<Language> _text;
    private int _version;

    /// <summary>Initializes the view model and starts following the channels.</summary>
    /// <param name="farm">The farm channel (answers to item use).</param>
    /// <param name="inventory">The inventory view model (refused moves).</param>
    /// <param name="text">UI text.</param>
    public GameMessagesViewModel(FarmChannel farm, InventoryViewModel inventory, IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(farm);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(text);

        _farm = farm;
        _inventory = inventory;
        _text = text;
        farm.UseCompleted += this.OnUseCompleted;
        inventory.Refused += this.Show;
    }

    /// <summary>Initializes the view model and also reports what the shipping bin paid (or why it paid nothing).</summary>
    /// <param name="farm">The farm channel (answers to item use).</param>
    /// <param name="inventory">The inventory view model (refused moves).</param>
    /// <param name="text">UI text.</param>
    /// <param name="market">The market channel (answers to shipping).</param>
    public GameMessagesViewModel(FarmChannel farm, InventoryViewModel inventory, IStringLocalizer<Language> text, MarketChannel market)
        : this(farm, inventory, text)
    {
        ArgumentNullException.ThrowIfNull(market);
        market.Completed += this.OnMarketCompleted;
    }

    /// <summary>Raised when <see cref="Message"/> changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Gets the message to show, or <see langword="null"/> when there is none.</summary>
    public string? Message { get; private set; }

    /// <summary>Maps a status code to its localized text.</summary>
    /// <param name="status">The server's outcome code.</param>
    /// <returns>The text, or <see langword="null"/> for <see cref="FarmStatus.Ok"/>.</returns>
    public string? TextOf(FarmStatus status) => status == FarmStatus.Ok ? null : _text["Farm_Status_" + status].Value;

    /// <inheritdoc/>
    public void Dispose()
    {
        _farm.UseCompleted -= this.OnUseCompleted;
        _inventory.Refused -= this.Show;
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

    private void OnUseCompleted(FarmStatus status, FarmAction action)
    {
        _ = action;
        if (this.TextOf(status) is { } text)
        {
            this.Show(text);
        }
    }

    private void Show(string text)
    {
        int version = ++_version;
        this.Message = text;
        this.Changed?.Invoke();
        _ = this.ClearLaterAsync(version);
    }

    private async Task ClearLaterAsync(int version)
    {
        await Task.Delay(MessageDuration).ConfigureAwait(false);
        if (version == _version)
        {
            this.Message = null;
            this.Changed?.Invoke();
        }
    }
}

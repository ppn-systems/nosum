// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels;

/// <summary>
/// Base of the panel view models (market, wardrobe, trade, co-op): holds the UI text lookup, raises <see cref="Changed"/>, and keeps the
/// one-line status message every panel shows under its content. Text always comes from <c>Language.resx</c> by key.
/// </summary>
public abstract class PanelViewModel : IObservableViewModel
{
    private readonly IStringLocalizer<Language> _text;

    /// <summary>Initializes the base.</summary>
    /// <param name="text">UI text.</param>
    protected PanelViewModel(IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
    }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>Gets the status line (the last outcome, localized), or an empty string.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>Gets whether the status line reports a success (the control shows it in the "ok" colour, not the refusal colour).</summary>
    public bool StatusIsGood { get; private set; }

    /// <summary>Looks up UI text.</summary>
    /// <param name="key">The <c>Language.resx</c> key.</param>
    /// <returns>The text in the current language.</returns>
    public string Text(string key) => _text[key].Value;

    /// <summary>Looks up UI text with arguments formatted in the current culture.</summary>
    /// <param name="key">The <c>Language.resx</c> key; the text is a composite format string.</param>
    /// <param name="args">The arguments.</param>
    /// <returns>The formatted text.</returns>
    public string Text(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, _text[key].Value, args);

    /// <summary>Formats a number of coins for display in the current culture.</summary>
    /// <param name="coins">The amount.</param>
    /// <returns>For example <c>1,250</c>.</returns>
    public static string Number(long coins) => coins.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Clears the status line.</summary>
    public void ClearStatus() => this.SetStatus(string.Empty);

    /// <summary>Shows a status line.</summary>
    /// <param name="text">Already localized text.</param>
    /// <param name="good"><see langword="true"/> when the text reports a success rather than a refusal.</param>
    protected void SetStatus(string text, bool good = false)
    {
        this.Status = text;
        this.StatusIsGood = good && !string.IsNullOrEmpty(text);
        this.Raise();
    }

    /// <summary>Tells the control to render again.</summary>
    protected void Raise() => this.Changed?.Invoke();
}

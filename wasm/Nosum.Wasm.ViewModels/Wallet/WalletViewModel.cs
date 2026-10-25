// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Microsoft.Extensions.Localization;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Wallet;

/// <summary>View model of the coin counter on the screen: the server's balance, formatted for the current language.</summary>
public sealed class WalletViewModel : PanelViewModel, IDisposable
{
    private readonly WalletChannel _wallet;

    /// <summary>Initializes the view model.</summary>
    /// <param name="wallet">The coin balance mirror.</param>
    /// <param name="text">UI text.</param>
    public WalletViewModel(WalletChannel wallet, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(wallet);

        _wallet = wallet;
        wallet.Changed += this.Raise;
    }

    /// <summary>Gets the balance, formatted without a unit.</summary>
    public string Amount => Number(_wallet.Coins);

    /// <summary>Gets the localized caption that names the coins.</summary>
    public string Caption => this.Text("Wallet_Caption");

    /// <inheritdoc/>
    public void Dispose() => _wallet.Changed -= this.Raise;
}

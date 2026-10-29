// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Nosum.Contracts.Account;
using Nosum.Wasm.ViewModels.Account;

namespace Nosum.Wasm.Security;

/// <summary>
/// The Google and Microsoft sign-in of the account form, through <c>wwwroot/external-sign-in.js</c>. One-off calls on a button click
/// (never in the frame loop), so <see cref="IJSRuntime"/> is fine here. The script is imported on the first click.
/// </summary>
internal sealed class BrowserExternalSignIn : IExternalSignIn
{
    private const string ModulePath = "./external-sign-in.js";

    private readonly IJSRuntime _js;
    private readonly ExternalSignInOptions _options;
    private IJSObjectReference? _module;

    /// <summary>Initializes the sign-in.</summary>
    /// <param name="js">The JavaScript runtime of the page.</param>
    /// <param name="options">The public client ids.</param>
    public BrowserExternalSignIn(IJSRuntime js, ExternalSignInOptions options)
    {
        ArgumentNullException.ThrowIfNull(js);
        ArgumentNullException.ThrowIfNull(options);
        _js = js;
        _options = options;
    }

    /// <inheritdoc/>
    public async Task<string> GetIdTokenAsync(ExternalProvider provider, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured(provider))
        {
            throw new ExternalSignInException(ExternalSignInFailure.NotConfigured);
        }

        string providerName = provider == ExternalProvider.Google ? "google" : "microsoft";
        string clientId = provider == ExternalProvider.Google ? _options.GoogleClientId : _options.MicrosoftClientId;
        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath).ConfigureAwait(false);
            return await _module.InvokeAsync<string>("signIn", cancellationToken, new object?[] { providerName, clientId, _options.MicrosoftTenant })
                .ConfigureAwait(false);
        }
        catch (JSException ex)
        {
            throw new ExternalSignInException(Map(ex.Message), ex);
        }
    }

    // The script rejects with one of these codes (external-sign-in.js); anything else is a plain failure.
    private static ExternalSignInFailure Map(string message)
    {
        if (message.Contains("cancelled", StringComparison.Ordinal))
        {
            return ExternalSignInFailure.Cancelled;
        }

        if (message.Contains("popup_blocked", StringComparison.Ordinal))
        {
            return ExternalSignInFailure.PopupBlocked;
        }

        if (message.Contains("not_displayed", StringComparison.Ordinal))
        {
            return ExternalSignInFailure.NotDisplayed;
        }

        if (message.Contains("not_configured", StringComparison.Ordinal))
        {
            return ExternalSignInFailure.NotConfigured;
        }

        return ExternalSignInFailure.Failed;
    }
}

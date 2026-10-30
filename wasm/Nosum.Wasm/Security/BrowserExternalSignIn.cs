// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Wasm.ViewModels.Account;

namespace Nosum.Wasm.Security;

/// <summary>
/// The Google and Microsoft sign-in of the account form, through <c>wwwroot/external-sign-in.js</c>. One-off calls on a button click
/// (never in the frame loop), so <see cref="IJSRuntime"/> is fine here. The script is imported on the first use.
/// </summary>
internal sealed class BrowserExternalSignIn : IExternalSignIn
{
    private const string ModulePath = "./external-sign-in.js";

    private readonly IJSRuntime _js;
    private readonly ExternalSignInOptions _options;
    private IJSObjectReference? _module;
    private DotNetObjectReference<BrowserExternalSignIn>? _self;
    private Func<string, Task>? _onGoogleIdToken;
    private Func<ExternalSignInFailure, Task>? _onGoogleFailure;

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
        if (provider != ExternalProvider.Microsoft)
        {
            // Google signs in through the button drawn by RenderGoogleButtonAsync, never through a token request.
            throw new ExternalSignInException(ExternalSignInFailure.Failed);
        }

        if (!_options.IsConfigured(provider))
        {
            throw new ExternalSignInException(ExternalSignInFailure.NotConfigured);
        }

        try
        {
            IJSObjectReference module = await ImportAsync().ConfigureAwait(false);
            return await module.InvokeAsync<string>("signIn", cancellationToken, new object?[] { "microsoft", _options.MicrosoftClientId, _options.MicrosoftTenant })
                .ConfigureAwait(false);
        }
        catch (JSException ex)
        {
            throw new ExternalSignInException(Map(ex.Message), ex);
        }
    }

    /// <inheritdoc/>
    public async Task RenderGoogleButtonAsync(string elementId, GameLanguage language, Func<string, Task> onIdToken, Func<ExternalSignInFailure, Task> onFailure)
    {
        ArgumentException.ThrowIfNullOrEmpty(elementId);
        ArgumentNullException.ThrowIfNull(onIdToken);
        ArgumentNullException.ThrowIfNull(onFailure);

        if (!_options.IsConfigured(ExternalProvider.Google))
        {
            await onFailure(ExternalSignInFailure.NotConfigured).ConfigureAwait(false);
            return;
        }

        _onGoogleIdToken = onIdToken;
        _onGoogleFailure = onFailure;
        _self ??= DotNetObjectReference.Create(this);
        try
        {
            IJSObjectReference module = await ImportAsync().ConfigureAwait(false);
            string locale = language == GameLanguage.English ? "en" : "vi";
            await module.InvokeVoidAsync("renderGoogleButton", elementId, _options.GoogleClientId, locale, _self).ConfigureAwait(false);
        }
        catch (JSException ex)
        {
            await onFailure(Map(ex.Message)).ConfigureAwait(false);
        }
    }

    /// <summary>Receives the Google ID token from the rendered button (called by <c>external-sign-in.js</c>).</summary>
    /// <param name="idToken">The ID token (a signed JWT). Never log it.</param>
    /// <returns>A task that completes when the token has been handed to the view model.</returns>
    [JSInvokable]
    public Task OnGoogleCredential(string idToken)
    {
        Func<string, Task>? handler = _onGoogleIdToken;
        return handler is null ? Task.CompletedTask : handler(idToken);
    }

    /// <summary>Receives a Google button failure (called by <c>external-sign-in.js</c>).</summary>
    /// <param name="code">The code the script reported; see <see cref="Map"/>.</param>
    /// <returns>A task that completes when the failure has been handed to the view model.</returns>
    [JSInvokable]
    public Task OnGoogleFailure(string code)
    {
        Func<ExternalSignInFailure, Task>? handler = _onGoogleFailure;
        return handler is null ? Task.CompletedTask : handler(Map(code));
    }

    private async Task<IJSObjectReference> ImportAsync()
    {
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath).ConfigureAwait(false);
        return _module;
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

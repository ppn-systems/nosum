// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>
/// Shows a Google or Microsoft sign-in and returns the ID token the provider issued. The browser implementation lives in
/// <c>Nosum.Wasm</c> (it calls <c>external-sign-in.js</c>); view models only see this interface.
/// </summary>
public interface IExternalSignIn
{
    /// <summary>
    /// Runs the Microsoft sign-in in a popup and returns its ID token. The token is a bearer credential: never log it.
    /// Google does not go through here: its button is drawn by <see cref="RenderGoogleButtonAsync"/>.
    /// </summary>
    /// <param name="provider">The provider to sign in with; only <see cref="ExternalProvider.Microsoft"/> is supported here.</param>
    /// <param name="cancellationToken">Cancels the wait for the provider.</param>
    /// <returns>The ID token (a signed JWT).</returns>
    /// <exception cref="ExternalSignInException">The player cancelled, the window was blocked, or the provider failed.</exception>
    Task<string> GetIdTokenAsync(ExternalProvider provider, CancellationToken cancellationToken);

    /// <summary>
    /// Draws Google's own sign-in button into the element with the given id. A click opens Google's popup, so the player always gets a window.
    /// The token (or the failure) comes back through the callbacks; the button stays until the page is gone.
    /// </summary>
    /// <param name="elementId">The id of the element the button is drawn into.</param>
    /// <param name="language">The UI language; the button text follows it.</param>
    /// <param name="onIdToken">Called with the Google ID token when the player signs in. The token is a bearer credential: never log it.</param>
    /// <param name="onFailure">Called when the button could not be drawn or Google returned an error.</param>
    /// <returns>A task that completes when the button is drawn, or when the failure has been reported.</returns>
    Task RenderGoogleButtonAsync(string elementId, GameLanguage language, Func<string, Task> onIdToken, Func<ExternalSignInFailure, Task> onFailure);
}

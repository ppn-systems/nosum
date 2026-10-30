// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Wasm.ViewModels.Account;

namespace Nosum.UnitTests.Wasm;

/// <summary>Stands in for the browser's Google and Microsoft sign-in: returns a fixed token, or fails with <see cref="Failure"/>. Google's button is
/// drawn by the fake too: <see cref="ClickGoogleButton"/> plays the player's sign-in through it.</summary>
internal sealed class FakeExternalSignIn : IExternalSignIn
{
    /// <summary>Gets the providers the form asked a token for, in order.</summary>
    public List<ExternalProvider> Requested { get; } = [];

    /// <summary>Gets or sets the token returned when the sign-in succeeds.</summary>
    public string Token { get; set; } = "id-token";

    /// <summary>Gets or sets the failure to raise instead of returning a token.</summary>
    public ExternalSignInFailure? Failure { get; set; }

    /// <summary>Gets the element id Google's button was drawn into, or null before the first draw.</summary>
    public string? GoogleElementId { get; private set; }

    /// <summary>Gets the language Google's button was drawn in.</summary>
    public GameLanguage? GoogleLanguage { get; private set; }

    private Func<string, Task>? _onGoogleIdToken;
    private Func<ExternalSignInFailure, Task>? _onGoogleFailure;

    /// <summary>Plays the player clicking Google's button and finishing in its popup: the token goes to the form, as the browser's callback would send it.</summary>
    /// <param name="idToken">The Google ID token the popup returned.</param>
    /// <returns>A task that completes when the form has handled the token.</returns>
    public Task ClickGoogleButton(string idToken) => _onGoogleIdToken is { } handler ? handler(idToken) : Task.CompletedTask;

    /// <summary>Plays Google's button failing to draw, or Google reporting an error.</summary>
    /// <param name="failure">The failure reported to the form.</param>
    /// <returns>A task that completes when the form has handled the failure.</returns>
    public Task FailGoogleButton(ExternalSignInFailure failure) => _onGoogleFailure is { } handler ? handler(failure) : Task.CompletedTask;

    /// <inheritdoc/>
    public Task RenderGoogleButtonAsync(string elementId, GameLanguage language, Func<string, Task> onIdToken, Func<ExternalSignInFailure, Task> onFailure)
    {
        this.GoogleElementId = elementId;
        this.GoogleLanguage = language;
        this.RenderedGoogleButtons++;
        _onGoogleIdToken = onIdToken;
        _onGoogleFailure = onFailure;
        return Task.CompletedTask;
    }

    /// <summary>Gets how many times Google's button was drawn.</summary>
    public int RenderedGoogleButtons { get; private set; }

    /// <inheritdoc/>
    public Task<string> GetIdTokenAsync(ExternalProvider provider, CancellationToken cancellationToken)
    {
        this.Requested.Add(provider);
        return this.Failure is { } failure
            ? Task.FromException<string>(new ExternalSignInException(failure))
            : Task.FromResult(this.Token);
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Account;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>
/// Shows a Google or Microsoft sign-in and returns the ID token the provider issued. The browser implementation lives in
/// <c>Nosum.Wasm</c> (it calls <c>external-sign-in.js</c>); view models only see this interface.
/// </summary>
public interface IExternalSignIn
{
    /// <summary>Runs the provider's sign-in and returns its ID token. The token is a bearer credential: never log it.</summary>
    /// <param name="provider">The provider to sign in with.</param>
    /// <param name="cancellationToken">Cancels the wait for the provider.</param>
    /// <returns>The ID token (a signed JWT).</returns>
    /// <exception cref="ExternalSignInException">The player cancelled, the window was blocked, or the provider failed.</exception>
    Task<string> GetIdTokenAsync(ExternalProvider provider, CancellationToken cancellationToken);
}

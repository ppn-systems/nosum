// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Account;
using Nosum.Wasm.ViewModels.Account;

namespace Nosum.UnitTests.Wasm;

/// <summary>Stands in for the browser's Google and Microsoft sign-in: returns a fixed token, or fails with <see cref="Failure"/>.</summary>
internal sealed class FakeExternalSignIn : IExternalSignIn
{
    /// <summary>Gets the providers the form asked a token for, in order.</summary>
    public List<ExternalProvider> Requested { get; } = [];

    /// <summary>Gets or sets the token returned when the sign-in succeeds.</summary>
    public string Token { get; set; } = "id-token";

    /// <summary>Gets or sets the failure to raise instead of returning a token.</summary>
    public ExternalSignInFailure? Failure { get; set; }

    /// <inheritdoc/>
    public Task<string> GetIdTokenAsync(ExternalProvider provider, CancellationToken cancellationToken)
    {
        this.Requested.Add(provider);
        return this.Failure is { } failure
            ? Task.FromException<string>(new ExternalSignInException(failure))
            : Task.FromResult(this.Token);
    }
}

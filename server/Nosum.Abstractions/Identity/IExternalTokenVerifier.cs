// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Account;

namespace Nosum.Abstractions.Identity;

/// <summary>Verifies the ID tokens of one identity provider (docs/adr/ADR-069). A token is trusted only after its signature and claims check out.</summary>
public interface IExternalTokenVerifier
{
    /// <summary>Gets the provider whose tokens this verifier accepts.</summary>
    ExternalProvider Provider { get; }

    /// <summary>Verifies an ID token.</summary>
    /// <param name="idToken">The compact JWS the client obtained from the provider.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The verified identity, or null when the token is not valid for this server, or the provider is not configured. Never throws for a bad token.</returns>
    Task<ExternalIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken);
}

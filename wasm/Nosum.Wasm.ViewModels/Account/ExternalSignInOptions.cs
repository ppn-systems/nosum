// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Contracts.Account;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>
/// The public client ids of the identity providers, read from the <c>ExternalSignIn</c> section of <c>appsettings.json</c>
/// (docs/adr/ADR-069). They identify this app to Google and Microsoft; they are not secrets. The server must be configured with the
/// same values, or its audience check refuses the token.
/// </summary>
/// <param name="GoogleClientId">The Google OAuth client id; empty turns the Google button off.</param>
/// <param name="MicrosoftClientId">The Microsoft application (client) id; empty turns the Microsoft button off.</param>
/// <param name="MicrosoftTenant">The Microsoft tenant; <c>common</c> accepts work, school and personal accounts.</param>
public sealed record ExternalSignInOptions(string GoogleClientId, string MicrosoftClientId, string MicrosoftTenant)
{
    /// <summary>Gets the options with no provider configured: both buttons are disabled.</summary>
    public static ExternalSignInOptions None { get; } = new(string.Empty, string.Empty, "common");

    /// <summary>Tells whether a provider has a client id, so its button can be used.</summary>
    /// <param name="provider">The provider.</param>
    /// <returns>True when the provider's client id is set.</returns>
    public bool IsConfigured(ExternalProvider provider) => provider switch
    {
        ExternalProvider.Google => !string.IsNullOrWhiteSpace(this.GoogleClientId),
        ExternalProvider.Microsoft => !string.IsNullOrWhiteSpace(this.MicrosoftClientId),
        _ => false,
    };
}

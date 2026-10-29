// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Account;

/// <summary>
/// The identity provider that issued an ID token (docs/adr/ADR-069). Values are append-only: a provider is never renumbered.
/// </summary>
public enum ExternalProvider : byte
{
    /// <summary>Google Identity (accounts.google.com).</summary>
    Google = 1,

    /// <summary>Microsoft identity platform (login.microsoftonline.com): work, school and personal accounts.</summary>
    Microsoft = 2,
}

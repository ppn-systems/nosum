// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>How the remembered player signed in: the icon on the continue card.</summary>
public enum RememberedAccountKind
{
    /// <summary>A guest started by the "play as guest" button.</summary>
    Guest = 0,

    /// <summary>Signed in with Google.</summary>
    Google = 1,

    /// <summary>Signed in with Microsoft.</summary>
    Microsoft = 2,
}

/// <summary>
/// The sign-in this browser remembers (docs/adr/ADR-082): the session token the server issued, its expiry, and the name and provider to show.
/// It never holds the provider's ID token. The token is a bearer credential: never log it.
/// </summary>
/// <param name="SessionToken">The token for <see cref="IAccountClient.ResumeAsync"/>.</param>
/// <param name="ExpiresAt">When the token expires (a resume rolls it forward).</param>
/// <param name="Nickname">The nickname as the server sent it.</param>
/// <param name="Kind">How the player signed in.</param>
public sealed record RememberedSession(string SessionToken, DateTimeOffset ExpiresAt, string Nickname, RememberedAccountKind Kind);

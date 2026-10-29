// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>Why a Google or Microsoft sign-in produced no ID token. Each value has its own localized text.</summary>
public enum ExternalSignInFailure
{
    /// <summary>The provider could not be used: the script failed to load or the provider returned an error.</summary>
    Failed = 0,

    /// <summary>The player closed or dismissed the sign-in.</summary>
    Cancelled = 1,

    /// <summary>The browser blocked the sign-in window (Microsoft popup).</summary>
    PopupBlocked = 2,

    /// <summary>Google did not show its sign-in prompt (cookies, settings or no signed-in account).</summary>
    NotDisplayed = 3,

    /// <summary>No client id is configured for the provider on this client.</summary>
    NotConfigured = 4,
}

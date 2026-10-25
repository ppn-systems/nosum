// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Account;

/// <summary>Outcome code of an account request. The server sends codes, never display text: the client maps
/// each code to localized text. Values are append-only.</summary>
public enum AccountStatus : byte
{
    /// <summary>The request succeeded; the session fields of the reply are set.</summary>
    Ok = 0,

    /// <summary>The username breaks <see cref="AccountLimits"/> (register only).</summary>
    InvalidUsername = 1,

    /// <summary>The password breaks <see cref="AccountLimits"/> (register only).</summary>
    InvalidPassword = 2,

    /// <summary>An account with this username already exists (register only).</summary>
    UsernameTaken = 3,

    /// <summary>Unknown username or wrong password (login). The two are deliberately indistinguishable.</summary>
    InvalidCredentials = 4,

    /// <summary>The session token is unknown, expired or revoked (resume).</summary>
    InvalidToken = 5,

    /// <summary>The language tag is not a well-formed BCP-47 tag (register, set language).</summary>
    InvalidLanguage = 6,
}

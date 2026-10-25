// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Account;

/// <summary>Outcome code of an account request. The server sends codes, never display text: the client maps
/// each code to localized text. Values are append-only.</summary>
public enum AccountStatus : byte
{
    /// <summary>The request succeeded; the session fields of the reply are set.</summary>
    Ok = 0,

    /// <summary>The e-mail address is not a valid <c>@gmail.com</c> address under <see cref="AccountLimits"/> (register, upgrade).</summary>
    InvalidEmail = 1,

    /// <summary>The password breaks <see cref="AccountLimits"/> (register, upgrade).</summary>
    InvalidPassword = 2,

    /// <summary>An account with this e-mail address already exists (register, upgrade).</summary>
    EmailTaken = 3,

    /// <summary>Unknown e-mail or wrong password (login). The two are deliberately indistinguishable.</summary>
    InvalidCredentials = 4,

    /// <summary>The session token is unknown, expired or revoked (resume).</summary>
    InvalidToken = 5,

    /// <summary>The language value is not a defined <see cref="Common.GameLanguage"/> (register, guest, set language).</summary>
    InvalidLanguage = 6,

    /// <summary>The session is not a guest session, so there is nothing to upgrade (upgrade only).</summary>
    NotGuest = 7,

    /// <summary>
    /// The player signed in on another connection, which ended this one (sent in <c>SessionEnded</c>, never as a request reply).
    /// </summary>
    SessionReplaced = 8,

    /// <summary>The nickname breaks the rules: length, characters (unaccented ASCII letters and digits only) or separators (register, upgrade, set nickname).</summary>
    InvalidNickname = 9,

    /// <summary>The nickname is on the reserved list or imitates a guest name (register, upgrade, set nickname).</summary>
    NicknameReserved = 10,

    /// <summary>Another player already has this nickname, compared case-insensitively (register, upgrade, set nickname).</summary>
    NicknameTaken = 11,

    /// <summary>The nickname was changed less than 24 hours ago (set nickname); the reply carries when the next change is allowed.</summary>
    NicknameTooSoon = 12,

    /// <summary>The nickname contains profanity or a slur (register, upgrade, set nickname).</summary>
    NicknameProfane = 13,

    /// <summary>The player is a guest: only registered accounts may choose or change a nickname (set nickname). A guest picks theirs when registering.</summary>
    NicknameRequiresAccount = 14,

    /// <summary>The recovery code is wrong, expired, already used or locked after too many attempts. The causes are deliberately indistinguishable (confirm password reset).</summary>
    InvalidResetCode = 15,

    /// <summary>Too many recovery or registration requests from this account or address; try again later (request password reset, register, start guest).</summary>
    TooManyRequests = 16,
}

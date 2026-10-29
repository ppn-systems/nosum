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

    /// <summary>Retired with account recovery: never sent. The value stays reserved.</summary>
    InvalidResetCode = 15,

    /// <summary>Too many registration requests from this address; try again later (register, start guest).</summary>
    TooManyRequests = 16,

    /// <summary>Retired with account recovery: never sent. The value stays reserved.</summary>
    SessionRevoked = 17,

    /// <summary>The account is banned by an admin (login, resume); the reply carries no session (docs/adr/ADR-031 D7).</summary>
    Banned = 18,

    /// <summary>An admin ended this connection (sent in <c>SessionEnded</c>, never as a request reply). The player may sign in again unless they were banned.</summary>
    SessionKicked = 19,
}

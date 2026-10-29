// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Account;

/// <summary>Outcome code of an account request. The server sends codes, never display text: the client maps
/// each code to localized text. Values are append-only.</summary>
public enum AccountStatus : byte
{
    /// <summary>The request succeeded; the session fields of the reply are set.</summary>
    Ok = 0,

    /// <summary>Retired with password accounts (docs/adr/ADR-069): never sent. The value stays reserved.</summary>
    InvalidEmail = 1,

    /// <summary>Retired with password accounts (docs/adr/ADR-069): never sent. The value stays reserved.</summary>
    InvalidPassword = 2,

    /// <summary>Retired with password accounts (docs/adr/ADR-069): never sent. The value stays reserved.</summary>
    EmailTaken = 3,

    /// <summary>Retired with password accounts (docs/adr/ADR-069): never sent. The value stays reserved.</summary>
    InvalidCredentials = 4,

    /// <summary>The session token is unknown, expired or revoked (resume).</summary>
    InvalidToken = 5,

    /// <summary>The language value is not a defined <see cref="Common.GameLanguage"/> (sign in, guest, set language).</summary>
    InvalidLanguage = 6,

    /// <summary>The session is not a guest session, so there is nothing to upgrade (link only).</summary>
    NotGuest = 7,

    /// <summary>
    /// The player signed in on another connection, which ended this one (sent in <c>SessionEnded</c>, never as a request reply).
    /// </summary>
    SessionReplaced = 8,

    /// <summary>The nickname breaks the rules: length, characters (unaccented ASCII letters and digits only) or separators (link, set nickname).</summary>
    InvalidNickname = 9,

    /// <summary>The nickname is on the reserved list or imitates a guest name (link, set nickname).</summary>
    NicknameReserved = 10,

    /// <summary>Another player already has this nickname, compared case-insensitively (link, set nickname).</summary>
    NicknameTaken = 11,

    /// <summary>The nickname was changed less than 24 hours ago (set nickname); the reply carries when the next change is allowed.</summary>
    NicknameTooSoon = 12,

    /// <summary>The nickname contains profanity or a slur (link, set nickname).</summary>
    NicknameProfane = 13,

    /// <summary>The player is a guest: only registered accounts may choose or change a nickname (set nickname). A guest links an identity to get one.</summary>
    NicknameRequiresAccount = 14,

    /// <summary>Retired with account recovery: never sent. The value stays reserved.</summary>
    InvalidResetCode = 15,

    /// <summary>Too many sign-in or guest requests from this address; try again later (sign in, start guest).</summary>
    TooManyRequests = 16,

    /// <summary>Retired with account recovery: never sent. The value stays reserved.</summary>
    SessionRevoked = 17,

    /// <summary>The account is banned by an admin (sign in, resume); the reply carries no session (docs/adr/ADR-031 D7).</summary>
    Banned = 18,

    /// <summary>An admin ended this connection (sent in <c>SessionEnded</c>, never as a request reply). The player may sign in again unless they were banned.</summary>
    SessionKicked = 19,

    /// <summary>The Google or Microsoft ID token failed verification: bad signature, unknown key, wrong issuer or audience, expired, or the provider is not configured on the server (sign in, link).</summary>
    InvalidIdToken = 20,

    /// <summary>The Google or Microsoft account is already linked to another player (link).</summary>
    IdentityTaken = 21,
}

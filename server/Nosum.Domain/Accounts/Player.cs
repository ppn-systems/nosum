// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Common;
using Nosum.Domain.Common;

namespace Nosum.Domain.Accounts;

/// <summary>A player account.</summary>
public sealed class Player : Entity
{
    /// <summary>Gets or sets the login e-mail address as typed (a <c>@gmail.com</c> address); null for a guest.</summary>
    public string? Email { get; set; }

    /// <summary>Gets or sets the lower-case form of <see cref="Email"/>; unique when set, used for lookups.</summary>
    public string? NormalizedEmail { get; set; }

    /// <summary>Gets or sets the self-describing password hash (algorithm, iterations, salt, hash); null for a guest. Never the password.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Gets or sets the display nickname as chosen (NFC), or a guest's neutral name (<c>guest-1234</c>). Every player has one;
    /// see <c>Nosum.Gameplay.Players.NicknameRules</c> (docs/adr/ADR-009 amendments).
    /// </summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>Gets or sets the unique, case-insensitive key of <see cref="Nickname"/> (<c>NicknameRules.Key</c>).</summary>
    public string NormalizedNickname { get; set; } = string.Empty;

    /// <summary>Gets or sets when the nickname was last changed with a rename request; null while it never was (a rename is then allowed at once).</summary>
    public DateTimeOffset? NicknameChangedAt { get; set; }

    /// <summary>Gets or sets whether this is a guest: no e-mail or password yet. A guest is upgraded in place, keeping <see cref="Entity.Id"/>.</summary>
    public bool IsGuest { get; set; }

    /// <summary>Gets or sets when the game unlocked the invitation to register (set by the quest feature); null until then.</summary>
    public DateTimeOffset? RegistrationPromptUnlockedAt { get; set; }

    /// <summary>Gets or sets the player's language; never <see cref="GameLanguage.Unspecified"/> once stored.</summary>
    public GameLanguage Language { get; set; } = GameLanguages.Default;

    /// <summary>Gets or sets when the account was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the player last signed in with a password or token; null until the first sign-in.</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Gets or sets when the player last came back or did something worth keeping the account for.
    /// Set at creation, on every sign-in and resume, and by <c>IPlayerActivityService</c>. Guests idle too long are deleted.
    /// </summary>
    public DateTimeOffset LastActiveAt { get; set; }

    /// <summary>Gets or sets the state needed to put the player back where they were.</summary>
    public PlayerState? State { get; set; }
}

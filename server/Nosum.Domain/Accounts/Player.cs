// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Common;
using Nosum.Domain.Common;

namespace Nosum.Domain.Accounts;

/// <summary>A player account.</summary>
public sealed class Player : Entity
{
    /// <summary>
    /// Gets or sets the e-mail the identity provider reported when the identity was linked, for display only; null when none was sent.
    /// Never an identity key: accounts are found through <see cref="ExternalLogin"/> (docs/adr/ADR-069).
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the display nickname as chosen (NFC), or a neutral name (<c>guest-1234</c>) given at creation. Every player has one;
    /// see <c>Nosum.Gameplay.Players.NicknameRules</c> (docs/adr/ADR-009 amendments).
    /// </summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>Gets or sets the unique, case-insensitive key of <see cref="Nickname"/> (<c>NicknameRules.Key</c>).</summary>
    public string NormalizedNickname { get; set; } = string.Empty;

    /// <summary>Gets or sets when the nickname was last changed with a rename request; null while it never was (a rename is then allowed at once).</summary>
    public DateTimeOffset? NicknameChangedAt { get; set; }

    /// <summary>Gets or sets whether this is a guest: no provider identity linked yet. A guest is upgraded in place, keeping <see cref="Entity.Id"/>.</summary>
    public bool IsGuest { get; set; }

    /// <summary>Gets or sets when the game unlocked the invitation to register (set by the quest feature); null until then.</summary>
    public DateTimeOffset? RegistrationPromptUnlockedAt { get; set; }

    /// <summary>Gets or sets the player's language; never <see cref="GameLanguage.Unspecified"/> once stored.</summary>
    public GameLanguage Language { get; set; } = GameLanguages.Default;

    /// <summary>Gets or sets when the account was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the player last signed in or resumed; null until the first sign-in.</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Gets or sets when the player last came back or did something worth keeping the account for.
    /// Set at creation, on every sign-in and resume, and by <c>IPlayerActivityService</c>. Guests idle too long are deleted.
    /// </summary>
    public DateTimeOffset LastActiveAt { get; set; }

    /// <summary>Gets or sets the account's role; <see cref="PlayerRole.Player"/> unless the host's <c>admin grant</c> command promoted it (docs/adr/ADR-031 D7).</summary>
    public PlayerRole Role { get; set; } = PlayerRole.Player;

    /// <summary>Gets or sets when the account was banned; null while it is not. A banned account cannot sign in or resume.</summary>
    public DateTimeOffset? BannedAt { get; set; }

    /// <summary>Gets or sets until when the player may not chat; null or a past time means they may.</summary>
    public DateTimeOffset? MutedUntil { get; set; }

    /// <summary>Gets or sets the state needed to put the player back where they were.</summary>
    public PlayerState? State { get; set; }
}

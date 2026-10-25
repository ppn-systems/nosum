// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Players;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Player;

/// <summary>
/// Turns the nickname the server sent into what the player reads (docs/adr/ADR-010: no display text on the wire). A chosen nickname is shown as it is;
/// a guest's neutral name (<c>guest-1234</c>) gets the localized prefix ("Khách 1234", "Guest 1234").
/// </summary>
public sealed class NicknameFormatter
{
    private readonly IStringLocalizer<Language> _text;
    private readonly LookChannel _looks;

    /// <summary>Initializes the formatter.</summary>
    /// <param name="text">UI text.</param>
    /// <param name="looks">Where the nicknames of the players on the map are cached.</param>
    public NicknameFormatter(IStringLocalizer<Language> text, LookChannel looks)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(looks);
        _text = text;
        _looks = looks;
    }

    /// <summary>Formats a nickname for display in the current language.</summary>
    /// <param name="nickname">The nickname as the server sent it; may be empty.</param>
    /// <returns>The text to show; empty for an empty nickname.</returns>
    public string Display(string? nickname)
    {
        if (string.IsNullOrEmpty(nickname))
        {
            return string.Empty;
        }

        return NicknameRules.IsGuestName(nickname, out string number)
            ? string.Format(CultureInfo.CurrentCulture, _text["Nickname_Guest"].Value, number)
            : nickname;
    }

    /// <summary>Names a player on the map: their nickname, or "Player #id" until the server's look for them arrives.</summary>
    /// <param name="playerId">The player's id on the map.</param>
    /// <returns>The text to show.</returns>
    public string PlayerLabel(uint playerId)
        => _looks.TryGetNickname(playerId, out string? nickname)
            ? this.Display(nickname)
            : string.Format(CultureInfo.CurrentCulture, _text["Player_Label"].Value, playerId);
}

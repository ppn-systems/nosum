// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Common;

/// <summary>
/// A language the game is played in. The wire, the database and the client all use this enum, not
/// language tag strings. Values are append-only: a new language gets the next number; existing
/// numbers are never changed or reused. Use <see cref="GameLanguages"/> to map to and from BCP-47 tags.
/// </summary>
public enum GameLanguage : byte
{
    /// <summary>No choice made; treated as <see cref="Vietnamese"/>.</summary>
    Unspecified = 0,

    /// <summary>Vietnamese (<c>vi</c>), the default.</summary>
    Vietnamese = 1,

    /// <summary>English (<c>en</c>).</summary>
    English = 2,
}

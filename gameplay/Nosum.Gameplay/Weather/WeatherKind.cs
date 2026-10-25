// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Weather;

/// <summary>The weather of one game day (docs/adr/ADR-027). Values are stable: the client may index icons by them.</summary>
public enum WeatherKind
{
    /// <summary>A clear day.</summary>
    Sun = 0,

    /// <summary>Rain: waters every outdoor tilled tile for the day.</summary>
    Rain = 1,

    /// <summary>A storm (summer only): rains like <see cref="Rain"/>; lightning is out of scope until lightning rods exist.</summary>
    Storm = 2,

    /// <summary>Snow (winter only): falls on the client, waters nothing.</summary>
    Snow = 3,
}

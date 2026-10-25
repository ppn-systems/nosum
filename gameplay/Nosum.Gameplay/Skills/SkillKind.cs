// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Skills;

/// <summary>The three skills a player trains by working (docs/adr/ADR-023 D3). The numbers are also the wire values; append-only.</summary>
public enum SkillKind : byte
{
    /// <summary>Not a skill.</summary>
    None = 0,

    /// <summary>Tilling, watering and harvesting.</summary>
    Farming = 1,

    /// <summary>Chopping, cutting weeds and grass, picking forage.</summary>
    Foraging = 2,

    /// <summary>Breaking stones and ore.</summary>
    Mining = 3,
}

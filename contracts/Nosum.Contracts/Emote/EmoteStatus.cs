// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Emote;

/// <summary>Outcome code of an emote request. Values are append-only.</summary>
public enum EmoteStatus : byte
{
    /// <summary>Shown to the players near the sender.</summary>
    Ok = 0,

    /// <summary>The code names no emote on the wheel.</summary>
    InvalidEmote = 1,

    /// <summary>Too soon after the last emote; wait a moment.</summary>
    Cooldown = 2,

    /// <summary>The player is on no map.</summary>
    NotOnMap = 3,
}

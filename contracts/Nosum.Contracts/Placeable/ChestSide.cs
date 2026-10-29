// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Placeable;

/// <summary>Where a stack is, in a <see cref="C2S.ChestMove"/>. Values are append-only.</summary>
public enum ChestSide : byte
{
    /// <summary>The player's own inventory.</summary>
    Player = 0,

    /// <summary>The open chest.</summary>
    Chest = 1,
}

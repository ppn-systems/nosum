// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Movement;

/// <summary>Result of checking a claimed position with <see cref="MoveValidator"/>.</summary>
public enum MoveVerdict
{
    /// <summary>The claim is legal and was applied.</summary>
    Accepted = 0,

    /// <summary>The player would have to walk faster than <see cref="Walker.Speed"/> allows.</summary>
    TooFast = 1,

    /// <summary>The position is outside the map.</summary>
    OutOfMap = 2,

    /// <summary>The position, or every way to it, crosses a blocked tile.</summary>
    Blocked = 3,

    /// <summary>A number is not finite or the facing is unknown.</summary>
    Invalid = 4,
}

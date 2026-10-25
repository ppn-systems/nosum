// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Player;

/// <summary>Why the server refused a <see cref="C2S.MoveIntent"/>. A code, never text.</summary>
public enum MoveRefusal : byte
{
    /// <summary>The claimed position needs a higher speed than walking allows.</summary>
    TooFast = 1,

    /// <summary>The claimed position is outside the map.</summary>
    OutOfMap = 2,

    /// <summary>The claimed position, or the way to it, is inside a blocked tile.</summary>
    Blocked = 3,

    /// <summary>A value was not a finite number or the facing was not a known facing.</summary>
    Invalid = 4,
}

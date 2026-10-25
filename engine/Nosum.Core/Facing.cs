// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Core;

/// <summary>The four facings used for characters (docs/adr/ADR-005 D5).</summary>
public enum Facing
{
    /// <summary>Towards the camera.</summary>
    Down = 0,

    /// <summary>To the right.</summary>
    Right = 1,

    /// <summary>Away from the camera.</summary>
    Up = 2,

    /// <summary>To the left.</summary>
    Left = 3,
}

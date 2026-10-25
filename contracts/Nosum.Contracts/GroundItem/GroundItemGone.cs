// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.GroundItem;

/// <summary>Why a ground item left the ground.</summary>
public enum GroundItemGone : byte
{
    /// <summary>A player picked it up; the client may fly it to them.</summary>
    PickedUp = 0,

    /// <summary>It lay too long and vanished.</summary>
    Expired = 1,
}

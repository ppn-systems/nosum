// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Mines;

/// <summary>How far down the mine a player has been (docs/adr/ADR-026 D3): the one thing about the mine that is stored. Floors themselves live in memory only.</summary>
public sealed class PlayerMine
{
    /// <summary>Gets or sets the player; also this row's primary key.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the deepest floor the player ever reached (1 to 40). The elevator offers every fifth floor down to it.</summary>
    public int DeepestFloor { get; set; }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;

namespace Nosum.Domain.Accounts;

/// <summary>Where a player was last: what the server restores when the player signs in again.</summary>
public sealed class PlayerState
{
    /// <summary>Gets or sets the owning player; also this row's primary key (one state per player).</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the map the player is on.</summary>
    public string MapName { get; set; } = "Farm";

    /// <summary>Gets or sets the feet x position in world pixels.</summary>
    public double X { get; set; }

    /// <summary>Gets or sets the feet y position in world pixels.</summary>
    public double Y { get; set; }

    /// <summary>Gets or sets the direction the player faces.</summary>
    public Facing Facing { get; set; }

    /// <summary>Gets or sets when the state was last saved.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

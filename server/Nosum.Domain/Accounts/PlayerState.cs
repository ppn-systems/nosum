// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;

namespace Nosum.Domain.Accounts;

/// <summary>Where a player was last (what the server restores when the player signs in again) plus the player's energy and skill experience (docs/adr/ADR-023). A row made by the progress service before the first place was saved has an empty <see cref="MapName"/>, which counts as no saved place.</summary>
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

    /// <summary>Gets or sets the energy left; meaningful once <see cref="EnergyRefilledDay"/> is set (docs/adr/ADR-023 D4).</summary>
    public int Energy { get; set; }

    /// <summary>Gets or sets the game day the energy was last refilled on; <c>-1</c> means never, so the first read fills it.</summary>
    public long EnergyRefilledDay { get; set; } = -1;

    /// <summary>Gets or sets the cumulative farming experience; the level is derived from it and never stored.</summary>
    public int FarmingXp { get; set; }

    /// <summary>Gets or sets the cumulative foraging experience.</summary>
    public int ForagingXp { get; set; }

    /// <summary>Gets or sets the cumulative mining experience.</summary>
    public int MiningXp { get; set; }

    /// <summary>Gets or sets the cumulative combat experience.</summary>
    public int CombatXp { get; set; }

    /// <summary>Gets or sets the current health points.</summary>
    public int Hp { get; set; } = 100;

    /// <summary>Gets or sets the current mana points.</summary>
    public int Mp { get; set; } = 50;

    /// <summary>Gets or sets when the state was last saved.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

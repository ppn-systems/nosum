// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Nosum.Gameplay.Maps;

/// <summary>
/// Tiles of a map that are blocked until a community unlock is set (docs/adr/ADR-037 D4): the broken bridge of the Beach. The map file keeps them blocked; the
/// walk grid releases them while <see cref="Key"/> is unlocked (<see cref="Nosum.Gameplay.Movement.WalkGate"/>).
/// </summary>
/// <param name="Key">The unlock key (<see cref="Community.CommunityUnlockKeys"/>).</param>
/// <param name="Tiles">The tiles the unlock opens.</param>
public sealed record MapGate(string Key, IReadOnlyList<(int X, int Y)> Tiles);

/// <summary>A rectangle of a map (corners included) whose walkable tiles are forage spots of one pool (docs/adr/ADR-037 D3).</summary>
/// <param name="X0">Left tile.</param>
/// <param name="Y0">Top tile.</param>
/// <param name="X1">Right tile.</param>
/// <param name="Y1">Bottom tile.</param>
/// <param name="Pool">The forage pool of the spots (<see cref="Nodes.ForagePools"/>).</param>
public readonly record struct ForageArea(int X0, int Y0, int X1, int Y1, byte Pool);

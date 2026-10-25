// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Gameplay.GroundItems;

namespace Nosum.Abstractions.GroundItems;

/// <summary>
/// The server's source of seeded randomness for yields (docs/adr/ADR-021 D5). Every call returns a fresh <see cref="GameRandom"/>
/// whose seed is derived from the server seed, a caller-chosen salt and a counter, so rolls are independent, the clients never roll,
/// and a test that fixes the server seed gets the same drops every run.
/// </summary>
public interface IYieldRandom
{
    /// <summary>Creates a generator for one roll (a node broken, a crop harvested).</summary>
    /// <param name="salt">What the roll is about (for example a tile or node id), to keep unrelated rolls apart.</param>
    /// <returns>A new generator; use it on one thread.</returns>
    GameRandom Create(ulong salt = 0);
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Maps;

namespace Nosum.Abstractions.Presence;

/// <summary>
/// Decides who may enter a map instance. The rule today: a shared map is open to everybody, a private one only to its owner.
/// Visiting and invitations later replace the implementation with one that also consults an allow-list; callers do not change
/// (docs/adr/ADR-015).
/// </summary>
public interface IInstanceAccessPolicy
{
    /// <summary>Returns whether a player may enter an instance.</summary>
    /// <param name="instance">The place.</param>
    /// <param name="playerId">The entering player's account id.</param>
    /// <returns><see langword="true"/> when entry is allowed.</returns>
    bool CanEnter(MapInstance instance, Guid playerId);
}

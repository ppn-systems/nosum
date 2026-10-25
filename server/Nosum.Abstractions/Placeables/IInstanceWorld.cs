// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Maps;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;

namespace Nosum.Abstractions.Placeables;

/// <summary>
/// The per-instance layer on top of a map: what players placed there. Movement validation asks it for the grid to walk on, so placed
/// furniture collides on the server exactly as it does on the client. A map that nobody can place on is served from the map's own
/// shared grid with no extra work.
/// </summary>
public interface IInstanceWorld
{
    /// <summary>Loads what is persisted for an instance (once); call before the first <see cref="GridFor"/> of a player entering it.</summary>
    /// <param name="instance">The place.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the instance is ready.</returns>
    ValueTask PrepareAsync(MapInstance instance, CancellationToken cancellationToken = default);

    /// <summary>Returns the grid players walk on in an instance: the map's grid plus the footprints of placed objects. Never waits.</summary>
    /// <param name="instance">The place.</param>
    /// <param name="map">The instance's map.</param>
    /// <returns>The grid; the map's own grid when nothing is placed.</returns>
    WalkGrid GridFor(MapInstance instance, MapDefinition map);
}

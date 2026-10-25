// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Gameplay.Buildings;

namespace Nosum.Abstractions.Buildings;

/// <summary>
/// Where the animals of the buildings people are standing in walk (docs/adr/ADR-028 D4): the server chooses short straight walks inside the animals' area every few seconds and
/// tells the players inside. Nothing is simulated for a building nobody is in; positions are computed from the walk's start time when somebody comes in.
/// </summary>
public interface IAnimalWander
{
    /// <summary>Lets the animals of every occupied building choose their next walk when they are due.</summary>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <param name="cancellationToken">Cancels the tick.</param>
    /// <returns>A task that completes when the new walks were sent.</returns>
    Task TickAsync(long nowMs, CancellationToken cancellationToken = default);

    /// <summary>Returns where an animal is and where it is going.</summary>
    /// <param name="kind">The kind of building the animal lives in.</param>
    /// <param name="animalId">The animal.</param>
    /// <param name="nowMs">Server time, Unix milliseconds.</param>
    /// <returns>The position and the walk; an animal nobody has seen yet appears on a tile of its area.</returns>
    AnimalMotionView Where(BuildingKind kind, Guid animalId, long nowMs);

    /// <summary>Forgets an animal that left.</summary>
    /// <param name="animalId">The animal.</param>
    void Forget(Guid animalId);
}

/// <summary>Where an animal is: feet position in interior pixels and the straight walk it is on.</summary>
/// <param name="X">Feet x now.</param>
/// <param name="Y">Feet y now.</param>
/// <param name="TargetX">Where the walk ends.</param>
/// <param name="TargetY">Where the walk ends.</param>
/// <param name="MoveMs">Milliseconds the walk still takes; zero when standing.</param>
public readonly record struct AnimalMotionView(float X, float Y, float TargetX, float TargetY, int MoveMs);

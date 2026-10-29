// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Building;

namespace Nosum.Wasm.Socket.Building;

/// <summary>What a request of the building channel was.</summary>
public enum BuildingAction
{
    /// <summary>Buying a blueprint at the carpenter.</summary>
    Commission = 1,

    /// <summary>Buying an animal at the carpenter.</summary>
    BuyAnimal = 2,

    /// <summary>Putting feed into the feeding bench.</summary>
    Feed = 3,

    /// <summary>Petting an animal.</summary>
    Pet = 4,

    /// <summary>Collecting an animal's produce.</summary>
    Collect = 5,
}

/// <summary>The server's answer to a request.</summary>
/// <param name="Action">What was asked.</param>
/// <param name="Status">How it went.</param>
/// <param name="ItemId">The blueprint, the feed or the produce; zero when none.</param>
/// <param name="Friendship">The animal's friendship after petting; zero otherwise.</param>
/// <param name="AnimalId">The animal asked about, or <see cref="Guid.Empty"/>.</param>
public readonly record struct BuildingResultInfo(BuildingAction Action, BuildingStatus Status, int ItemId, int Friendship, Guid AnimalId);

/// <summary>One farm building as the client knows it: what the carpenter page lists and what the farm draws under construction (docs/adr/ADR-028).</summary>
/// <param name="Id">The building's id.</param>
/// <param name="ObjectId">The placed object standing on the land.</param>
/// <param name="Kind">The kind (<c>BuildingKind</c> in Nosum.Gameplay; this layer holds the wire byte).</param>
/// <param name="MapId">The land's map id.</param>
/// <param name="FinishesAt">When the building is finished, on the clock of this channel.</param>
/// <param name="Animals">Animals living in it.</param>
/// <param name="Capacity">Animals it holds.</param>
public readonly record struct BuildingEntry(Guid Id, int ObjectId, byte Kind, string MapId, DateTimeOffset FinishesAt, int Animals, int Capacity);

/// <summary>An animal of the building the player is in, drawn at a position computed from its walk.</summary>
/// <param name="Id">The animal's id.</param>
/// <param name="Kind">The species (<c>AnimalKind</c> in Nosum.Gameplay; this layer holds the wire byte).</param>
/// <param name="Name">Its name.</param>
/// <param name="Friendship">Friendship, 0 to 1000.</param>
/// <param name="Petted">Whether it was petted today.</param>
/// <param name="HasProduce">Whether it has produce to collect today.</param>
/// <param name="X">Feet x now, interior pixels.</param>
/// <param name="Y">Feet y now, interior pixels.</param>
/// <param name="Facing">0 down, 1 right, 2 up, 3 left: the way the walk goes.</param>
/// <param name="Walking">Whether it is on a walk now.</param>
public readonly record struct AnimalPose(Guid Id, byte Kind, string Name, int Friendship, bool Petted, bool HasProduce, double X, double Y, int Facing, bool Walking);

/// <summary>The last animal the player petted, so its hearts can float up for a moment.</summary>
/// <param name="AnimalId">The animal's id; empty before the first petting.</param>
/// <param name="Friendship">Its friendship after the petting.</param>
/// <param name="AtMs">When the petting was answered, from <c>BuildingChannel.NowMs</c>; zero before the first petting.</param>
public readonly record struct AnimalPetting(Guid AnimalId, int Friendship, long AtMs);

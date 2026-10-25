// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Building;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;

namespace Nosum.Abstractions.Buildings;

/// <summary>
/// Farm buildings and their animals (docs/adr/ADR-028). Every request takes the connection: the player, their place and their position are the server's own record,
/// never a value from the packet. The carpenter hands over blueprints and animals; the rest happens at home.
/// </summary>
public interface IBuildingService
{
    /// <summary>Buys a building's blueprint at the carpenter's counter: coins, wood and stone out, the blueprint in, in one transaction.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="kind">The building.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The outcome; <see cref="BuildingOutcome.ItemId"/> is the blueprint.</returns>
    Task<BuildingOutcome> CommissionAsync(IConnection connection, BuildingKind kind, CancellationToken cancellationToken = default);

    /// <summary>Lists the buildings of the place the player stands in (a farm or co-op land), or of their own lands.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="mine">Whether to list the player's own farm and co-op land instead of the place they stand in.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The buildings, with how long each has left to build.</returns>
    Task<IReadOnlyList<BuildingView>> ListAsync(IConnection connection, bool mine, CancellationToken cancellationToken = default);

    /// <summary>Buys an animal at the carpenter's counter and puts it in one of the player's finished buildings.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="buildingId">The building it will live in.</param>
    /// <param name="kind">The species.</param>
    /// <param name="name">The name the player gives it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The outcome; <see cref="BuildingOutcome.Id"/> is the new animal.</returns>
    Task<BuildingOutcome> BuyAnimalAsync(IConnection connection, Guid buildingId, AnimalKind kind, string? name, CancellationToken cancellationToken = default);

    /// <summary>Reads the animals of the building the player stands in.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The room, or <see langword="null"/> when the player is not in a building they may enter.</returns>
    Task<AnimalRoomView?> GetAnimalsAsync(IConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Puts feed from an inventory slot into the feeding bench of the building the player stands in.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="slot">The inventory slot.</param>
    /// <param name="quantity">How many to put in.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The outcome; <see cref="BuildingOutcome.Quantity"/> is the rations now in the bench.</returns>
    Task<BuildingOutcome> FeedAsync(IConnection connection, int slot, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Pets an animal; once a day each.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="animalId">The animal.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The outcome.</returns>
    Task<AnimalOutcome> PetAsync(IConnection connection, Guid animalId, CancellationToken cancellationToken = default);

    /// <summary>Collects an animal's produce for the day; exactly once.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="animalId">The animal.</param>
    /// <param name="slot">The inventory slot of the tool in hand; a cow needs the milk pail there.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The outcome.</returns>
    Task<AnimalOutcome> CollectAsync(IConnection connection, Guid animalId, int slot, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a commission, a purchase or a feeding.</summary>
/// <param name="Status">How it went.</param>
/// <param name="Id">The new animal's id for a purchase; empty otherwise.</param>
/// <param name="ItemId">The blueprint handed over, or the feed put in; zero otherwise.</param>
/// <param name="Quantity">How many; for a feeding the rations now in the bench.</param>
public readonly record struct BuildingOutcome(BuildingStatus Status, Guid Id, int ItemId, int Quantity)
{
    /// <summary>Returns a refusal.</summary>
    /// <param name="status">Why.</param>
    /// <returns>The outcome.</returns>
    public static BuildingOutcome Refused(BuildingStatus status) => new(status, Guid.Empty, 0, 0);
}

/// <summary>The outcome of petting or collecting.</summary>
/// <param name="Status">How it went.</param>
/// <param name="ItemId">The produce handed over; zero when none.</param>
/// <param name="Quantity">How many.</param>
/// <param name="Friendship">The animal's friendship now.</param>
public readonly record struct AnimalOutcome(BuildingStatus Status, int ItemId, int Quantity, int Friendship)
{
    /// <summary>Returns a refusal.</summary>
    /// <param name="status">Why.</param>
    /// <returns>The outcome.</returns>
    public static AnimalOutcome Refused(BuildingStatus status) => new(status, 0, 0, 0);
}

/// <summary>One building of a land.</summary>
/// <param name="Id">The building's id.</param>
/// <param name="ObjectId">The placed object that stands on the land.</param>
/// <param name="Kind">The kind.</param>
/// <param name="MapId">The land's map id.</param>
/// <param name="RemainingMs">Milliseconds of construction left; zero once finished.</param>
/// <param name="Animals">Animals living in it.</param>
/// <param name="Capacity">Animals it holds.</param>
public readonly record struct BuildingView(Guid Id, int ObjectId, BuildingKind Kind, string MapId, int RemainingMs, int Animals, int Capacity);

/// <summary>One animal of a building: the card and the walk it is on.</summary>
/// <param name="Id">The animal.</param>
/// <param name="Kind">The species.</param>
/// <param name="Name">Its name.</param>
/// <param name="Friendship">Friendship, 0 to 1000.</param>
/// <param name="Flags">Bit 0: petted today; bit 1: has produce.</param>
/// <param name="X">Feet x now, interior pixels.</param>
/// <param name="Y">Feet y now, interior pixels.</param>
/// <param name="TargetX">Where its walk ends.</param>
/// <param name="TargetY">Where its walk ends.</param>
/// <param name="MoveMs">Milliseconds the walk still takes.</param>
public readonly record struct AnimalView(Guid Id, AnimalKind Kind, string Name, int Friendship, byte Flags, float X, float Y, float TargetX, float TargetY, int MoveMs)
{
    /// <summary>The bit of <see cref="Flags"/> that says the animal was petted today.</summary>
    public const byte PettedFlag = 1;

    /// <summary>The bit of <see cref="Flags"/> that says the animal has produce to collect.</summary>
    public const byte ProduceFlag = 2;
}

/// <summary>The animals of the building a player stands in.</summary>
/// <param name="BuildingId">The building.</param>
/// <param name="Kind">The building kind.</param>
/// <param name="Rations">Rations in the feeding bench.</param>
/// <param name="MayTend">Whether the player may feed, pet and collect.</param>
/// <param name="Animals">The animals.</param>
public sealed record AnimalRoomView(Guid BuildingId, BuildingKind Kind, int Rations, bool MayTend, IReadOnlyList<AnimalView> Animals);

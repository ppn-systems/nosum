// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Pet;

namespace Nosum.Abstractions.Pets;

/// <summary>What the other players see of a pet: the kind (0 none, 1 cat, 2 dog) and the cleaned name.</summary>
/// <param name="Kind">The kind code.</param>
/// <param name="Name">The name; empty without a pet.</param>
public readonly record struct PetLook(byte Kind, string Name)
{
    /// <summary>Nothing: no pet.</summary>
    public static PetLook None => new(0, string.Empty);
}

/// <summary>The owner's view of their pet.</summary>
/// <param name="Status">The outcome of the request that produced the view.</param>
/// <param name="Kind">The kind code; 0 without a pet.</param>
/// <param name="Name">The name; empty without a pet.</param>
/// <param name="Mood">The mood, 0 to 100.</param>
/// <param name="PettedToday">Whether it was petted this game day.</param>
public readonly record struct PetView(PetStatus Status, byte Kind, string Name, int Mood, bool PettedToday);

/// <summary>
/// The pet companion (docs/adr/ADR-071 D3-D5): adoption (a cat or a dog, named with the chat profanity mask), one petting per game day for a
/// mood and energy bonus. Every change is one server transaction; the cached <see cref="PetLook"/> is what LookRow carries to the other players.
/// </summary>
public interface IPetService
{
    /// <summary>Reads the owner's pet.</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The pet, or a view with kind 0 when there is none.</returns>
    Task<PetView> GetAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Adopts a cat or a dog with a name. One pet at a time.</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="kind">The kind code: 1 cat, 2 dog.</param>
    /// <param name="name">The name as typed; cleaned and masked, at most 12 characters.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome with the pet as it stands.</returns>
    Task<PetView> AdoptAsync(Guid playerId, byte kind, string name, CancellationToken cancellationToken = default);

    /// <summary>Pets the player's pet: once a game day, for a mood bonus and an energy bonus.</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome with the pet as it stands.</returns>
    Task<PetView> PetAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>What the other players see of the player's pet (cached; read on every look).</summary>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The kind and name; none when there is no pet.</returns>
    ValueTask<PetLook> LookOfAsync(Guid playerId, CancellationToken cancellationToken = default);
}

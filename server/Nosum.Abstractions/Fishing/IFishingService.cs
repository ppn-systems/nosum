// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Fishing;

namespace Nosum.Abstractions.Fishing;

/// <summary>
/// Casting a fishing rod at water and reeling the line in (docs/adr/ADR-078). The server starts the cast on its own clock, judges the press on
/// the same clock and rolls the catch from a seed it made; the client only shows what these return.
/// </summary>
public interface IFishingService
{
    /// <summary>
    /// Casts the rod the player holds at a water tile. The rod, the water, the reach, the room for a catch and the energy are checked in that order; the
    /// energy is charged and the cast opened in one transaction.
    /// </summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="tileX">The water tile x.</param>
    /// <param name="tileY">The water tile y.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome; an open cast carries its id, bite delay and window.</returns>
    Task<CastOutcome> CastAsync(IConnection connection, int tileX, int tileY, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reels the line in. Before the bite the cast stays open; in the window the fish is hooked and rolled; after it the fish has escaped.
    /// </summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="castId">The cast id from <see cref="CastOutcome"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome; a catch is already in the inventory when this returns.</returns>
    Task<ReelOutcome> ReelInAsync(IConnection connection, uint castId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IFishingService.CastAsync"/>.</summary>
/// <param name="Status">Ok when the cast is open, otherwise why it was refused.</param>
/// <param name="CastId">The open cast's id; zero unless Ok.</param>
/// <param name="BiteDelayMs">Milliseconds from the cast to the bite; zero unless Ok.</param>
/// <param name="WindowMs">Milliseconds the press window stays open after the bite; zero unless Ok.</param>
public readonly record struct CastOutcome(FishingStatus Status, uint CastId = 0, int BiteDelayMs = 0, int WindowMs = 0);

/// <summary>Outcome of <see cref="IFishingService.ReelInAsync"/>.</summary>
/// <param name="Status">Ok (a fish), Nothing, TooEarly (cast kept), Escaped, NoCast, NoSpace or Conflict.</param>
/// <param name="ItemId">The fish caught; zero unless Ok.</param>
/// <param name="Quantity">How many were caught: one for Ok, otherwise zero.</param>
/// <param name="XpGained">Fishing experience the attempt gave; zero when the cast was not judged.</param>
/// <param name="SizeCm">The fish's size in centimetres, rolled from the cast seed; zero unless Ok.</param>
public readonly record struct ReelOutcome(FishingStatus Status, int ItemId = 0, int Quantity = 0, int XpGained = 0, int SizeCm = 0);

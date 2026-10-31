// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Inventory;

namespace Nosum.Abstractions.Economy;

/// <summary>What kind of holder a party of a transfer is.</summary>
public enum PartyKind
{
    /// <summary>A player: has an inventory and a coin balance.</summary>
    Player = 1,

    /// <summary>A co-op: has a warehouse (items only, no coins).</summary>
    Coop = 2,
}

/// <summary>Someone who gives or receives in a transfer.</summary>
/// <param name="Kind">Player or co-op.</param>
/// <param name="Id">The player's or the co-op's id.</param>
public readonly record struct TransferParty(PartyKind Kind, Guid Id)
{
    /// <summary>Creates a player party.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns>The party.</returns>
    public static TransferParty Player(Guid playerId) => new(PartyKind.Player, playerId);

    /// <summary>Creates a co-op party.</summary>
    /// <param name="coopId">The co-op.</param>
    /// <returns>The party.</returns>
    public static TransferParty Coop(Guid coopId) => new(PartyKind.Coop, coopId);
}

/// <summary>
/// One party's side of a transfer: a change of one item kind and of coins. Positive numbers are received, negative are given.
/// A transfer is a list of legs that must balance in the caller's mind (what one party gives another receives) - the service moves
/// exactly what the legs say, so a fee is simply a leg that nobody receives.
/// </summary>
/// <param name="Party">Who.</param>
/// <param name="ItemId">The item, or zero when the leg moves coins only.</param>
/// <param name="Items">How many items are received (positive) or given (negative).</param>
/// <param name="Coins">How many coins are received (positive) or given (negative); players only.</param>
public readonly record struct TransferLeg(TransferParty Party, int ItemId, int Items, long Coins = 0);

/// <summary>How a transfer ended.</summary>
public enum TransferStatus
{
    /// <summary>Everything moved.</summary>
    Ok = 0,

    /// <summary>A party gives more items than it has.</summary>
    NotEnoughItems = 1,

    /// <summary>A party gives more coins than it has.</summary>
    NotEnoughCoins = 2,

    /// <summary>A receiver has no room.</summary>
    NoSpace = 3,

    /// <summary>A balance would exceed the largest allowed.</summary>
    TooBig = 4,

    /// <summary>Another change to the same data won; nothing moved, ask again.</summary>
    Conflict = 5,

    /// <summary>The caller's enlisted work refused (it knows why); nothing moved.</summary>
    Refused = 6,

    /// <summary>The legs are malformed; nothing moved.</summary>
    Invalid = 7,
}

/// <summary>Extra work that joins a transfer's transaction. Return <see cref="TransferStatus.Ok"/> to commit, anything else to roll everything back.</summary>
/// <param name="unit">The unit of work the transfer is saved with.</param>
/// <param name="cancellationToken">Cancels the work.</param>
/// <returns>The status; <see cref="TransferStatus.Refused"/> when the caller records its own reason.</returns>
public delegate Task<TransferStatus> TransferEnlistment(IUnitOfWork unit, CancellationToken cancellationToken);

/// <summary>
/// The generic escrow/transfer: moves items and coins between players and co-op warehouses in ONE database transaction, serialized
/// per party, audited, and pushed to online players. The market (list, buy, cancel, expire), direct trades, the shipping bin and the
/// co-op warehouse are all transfers with a little enlisted work of their own (docs/adr/ADR-013).
/// </summary>
public interface ITransferService
{
    /// <summary>Executes legs all-or-nothing.</summary>
    /// <param name="legs">What moves.</param>
    /// <param name="reason">Stable audit code, for example <c>market.buy</c>; never display text.</param>
    /// <param name="enlist">Work for the same transaction (a listing row, a log line), or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok, or why nothing moved.</returns>
    Task<TransferStatus> ExecuteAsync(IReadOnlyList<TransferLeg> legs, string reason, TransferEnlistment? enlist = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes legs all-or-nothing and, in the same transaction, the inventory operations of one player (for example a backpack
    /// growth). The operations run after the player's removes and adds from the legs. The player is part of the transfer even when
    /// no leg names him, so the operations and the coins are saved together or not at all.
    /// </summary>
    /// <param name="playerId">The player whose inventory also changes by <paramref name="operations"/>.</param>
    /// <param name="operations">The inventory steps, applied in order after the legs.</param>
    /// <param name="legs">What moves.</param>
    /// <param name="reason">Stable audit code, for example <c>backpack.upgrade</c>; never display text.</param>
    /// <param name="enlist">Work for the same transaction, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok, or why nothing moved.</returns>
    Task<TransferStatus> ExecuteWithOperationsAsync(
        Guid playerId,
        IReadOnlyList<InventoryOperation> operations,
        IReadOnlyList<TransferLeg> legs,
        string reason,
        TransferEnlistment? enlist = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a player's coin balance; a player with no wallet yet gets one with the starting coins.</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The balance.</returns>
    Task<long> GetBalanceAsync(Guid playerId, CancellationToken cancellationToken = default);
}

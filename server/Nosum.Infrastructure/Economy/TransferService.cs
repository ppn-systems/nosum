// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Market.S2C;
using Nosum.Domain.Coops;
using Nosum.Domain.Economy;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;

namespace Nosum.Infrastructure.Economy;

/// <summary>
/// <see cref="ITransferService"/>: builds one <see cref="IInventoryService.ApplyManyAsync"/> call from the legs of the players, joins
/// the wallets and the co-op warehouses to its transaction through the enlistment, then the caller's own work. Because the inventory
/// service holds every involved player's lock and saves once, a transfer is atomic, and two transfers touching the same player
/// (two buyers of one listing, a trade and a sale) cannot interleave. The server is a single process (docs/adr/ADR-009), so the
/// in-process locks are the ordering; the concurrency tokens on balances, stock and listings are the backstop that turns a lost race
/// into <see cref="TransferStatus.Conflict"/>.
/// </summary>
public sealed class TransferService : ITransferService
{
    private const string StarterReason = "starter";

    private readonly IInventoryService _inventory;
    private readonly TimeProvider _time;
    private readonly IPlayerNotifier? _notifier;

    /// <summary>Initializes the service.</summary>
    /// <param name="inventory">Moves the players' items; owns the per-player locks.</param>
    /// <param name="time">The clock for audit timestamps; tests pass a fake one.</param>
    /// <param name="notifier">Pushes balance changes to online players; <see langword="null"/> to push nothing.</param>
    public TransferService(IInventoryService inventory, TimeProvider time, IPlayerNotifier? notifier = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(time);

        _inventory = inventory;
        _time = time;
        _notifier = notifier;
    }

    /// <inheritdoc/>
    public Task<TransferStatus> ExecuteAsync(IReadOnlyList<TransferLeg> legs, string reason, TransferEnlistment? enlist = null, CancellationToken cancellationToken = default)
        => this.ExecuteCoreAsync(-1, legs, Guid.Empty, [], reason, enlist, cancellationToken);

    /// <inheritdoc/>
    public Task<TransferStatus> ExecuteWithOperationsAsync(
        Guid playerId,
        IReadOnlyList<InventoryOperation> operations,
        IReadOnlyList<TransferLeg> legs,
        string reason,
        TransferEnlistment? enlist = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (playerId == Guid.Empty)
        {
            return Task.FromResult(TransferStatus.Invalid);
        }

        return this.ExecuteCoreAsync(-1, legs, playerId, operations, reason, enlist, cancellationToken);
    }

    /// <summary>
    /// Runs a transfer whose negative player item leg is taken from a specific inventory slot when <paramref name="sourceSlot"/> is non-negative.
    /// </summary>
    internal Task<TransferStatus> ExecuteFromSlotAsync(int sourceSlot, IReadOnlyList<TransferLeg> legs, string reason, TransferEnlistment? enlist = null, CancellationToken cancellationToken = default)
        => this.ExecuteCoreAsync(sourceSlot, legs, Guid.Empty, [], reason, enlist, cancellationToken);

    // extraPlayer (Guid.Empty for none) joins the transfer even without a leg, and extraOperations run after his removes and adds.
    private async Task<TransferStatus> ExecuteCoreAsync(
        int sourceSlot,
        IReadOnlyList<TransferLeg> legs,
        Guid extraPlayer,
        IReadOnlyList<InventoryOperation> extraOperations,
        string reason,
        TransferEnlistment? enlist,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(legs);
        ArgumentException.ThrowIfNullOrEmpty(reason);

        // Group the legs: per player the inventory operations and the net coins; per co-op the stock changes.
        Dictionary<Guid, List<InventoryOperation>> adds = [];
        Dictionary<Guid, List<InventoryOperation>> removes = [];
        Dictionary<Guid, Int128> coins = [];
        Dictionary<(Guid Coop, int Item), int> stock = [];
        List<Guid> players = [];

        foreach (TransferLeg leg in legs)
        {
            if (leg.Party.Id == Guid.Empty || (leg.Items != 0 && !ItemCatalog.TryGet(leg.ItemId, out _)) || (leg.Party.Kind == PartyKind.Coop && leg.Coins != 0))
            {
                return TransferStatus.Invalid;
            }

            if (leg.Party.Kind == PartyKind.Player)
            {
                if (!players.Contains(leg.Party.Id))
                {
                    players.Add(leg.Party.Id);
                    adds[leg.Party.Id] = [];
                    removes[leg.Party.Id] = [];
                }

                if (leg.Items > 0)
                {
                    adds[leg.Party.Id].Add(InventoryOperation.Add(leg.ItemId, leg.Items));
                }
                else if (leg.Items < 0)
                {
                    removes[leg.Party.Id].Add(
                        sourceSlot >= 0
                            ? InventoryOperation.RemoveFromSlot(sourceSlot, leg.ItemId, -leg.Items)
                            : InventoryOperation.Remove(leg.ItemId, -leg.Items));
                }

                coins[leg.Party.Id] = coins.GetValueOrDefault(leg.Party.Id) + leg.Coins;
            }
            else if (leg.Items != 0)
            {
                stock[(leg.Party.Id, leg.ItemId)] = stock.GetValueOrDefault((leg.Party.Id, leg.ItemId)) + leg.Items;
            }
        }

        if (extraPlayer != Guid.Empty && !players.Contains(extraPlayer))
        {
            players.Add(extraPlayer);
            adds[extraPlayer] = [];
            removes[extraPlayer] = [];
        }

        if (players.Count == 0)
        {
            return TransferStatus.Invalid;
        }

        // Removes first, so room freed by giving counts for receiving; the extra operations (a backpack growth) come last. A party whose legs are
        // coins only has no item operation and no extra operation, so its inventory is not loaded at all (a seller of a listing: docs/adr/ADR-035 D1).
        // If every party is coins only, one empty change is kept: the unit of work that carries the coin legs runs only when there is a change to apply.
        List<PlayerInventoryChange> changes = [];
        foreach (Guid player in players)
        {
            bool hasExtra = player == extraPlayer && extraOperations.Count > 0;
            if (removes[player].Count == 0 && adds[player].Count == 0 && !hasExtra)
            {
                continue;
            }

            List<InventoryOperation> operations = [.. removes[player], .. adds[player]];
            if (player == extraPlayer)
            {
                operations.AddRange(extraOperations);
            }

            changes.Add(new PlayerInventoryChange(player, operations));
        }

        if (changes.Count == 0)
        {
            changes.Add(new PlayerInventoryChange(players[0], []));
        }

        TransferStatus failure = TransferStatus.Ok;
        Dictionary<Guid, long> balances = [];
        InventoryStatus status = await _inventory.ApplyManyAsync(
            changes,
            reason,
            async (unit, token) =>
            {
                failure = await this.JoinAsync(unit, coins, stock, balances, reason, token).ConfigureAwait(false);
                if (failure == TransferStatus.Ok && enlist is not null)
                {
                    failure = await enlist(unit, token).ConfigureAwait(false);
                }

                return failure == TransferStatus.Ok;
            },
            cancellationToken).ConfigureAwait(false);

        TransferStatus result = status switch
        {
            InventoryStatus.Ok => TransferStatus.Ok,
            InventoryStatus.NotEnough or InventoryStatus.EmptySlot or InventoryStatus.InvalidSlot => TransferStatus.NotEnoughItems,
            InventoryStatus.NoSpace => TransferStatus.NoSpace,
            InventoryStatus.Conflict when failure != TransferStatus.Ok => failure,
            InventoryStatus.Conflict => TransferStatus.Conflict,
            _ => TransferStatus.Invalid,
        };

        if (result == TransferStatus.Ok)
        {
            // The balance each wallet was saved with: pushed as it is, not read again. It is not kept anywhere, so it is not a cache of a balance (docs/adr/ADR-035 D6).
            foreach ((Guid player, long balance) in balances)
            {
                await this.PushBalanceAsync(player, balance, cancellationToken).ConfigureAwait(false);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<long> GetBalanceAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        long balance = 0;
        _ = await _inventory.ApplyManyAsync(
            [new PlayerInventoryChange(playerId, [])],
            "wallet.read",
            async (unit, token) =>
            {
                PlayerWallet wallet = await this.WalletAsync(unit, playerId, token).ConfigureAwait(false);
                balance = wallet.Coins;
                return true;
            },
            cancellationToken).ConfigureAwait(false);
        return balance;
    }

    private async Task<TransferStatus> JoinAsync(IUnitOfWork unit, Dictionary<Guid, Int128> coins, Dictionary<(Guid Coop, int Item), int> stock, Dictionary<Guid, long> balances, string reason, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        Dictionary<Guid, PlayerWallet> wallets = await this.WalletsAsync(unit, coins.Keys, cancellationToken).ConfigureAwait(false);

        foreach ((Guid player, Int128 delta) in coins)
        {
            // The wallet is created (and the starting coins logged) even by a transfer that moves no coins for this player:
            // the balance row must exist before anything reads it.
            PlayerWallet wallet = wallets[player];
            if (delta == 0)
            {
                continue;
            }

            Int128 next = (Int128)wallet.Coins + delta;
            if (next < 0)
            {
                return TransferStatus.NotEnoughCoins;
            }

            if (next > CoinRules.MaxBalance || delta < long.MinValue || delta > long.MaxValue)
            {
                return TransferStatus.TooBig;
            }

            long nextCoins = (long)next;
            wallet.Coins = nextCoins;
            balances[player] = nextCoins;
            unit.Repository<CoinLogEntry>().Add(new CoinLogEntry { PlayerId = player, Delta = (long)delta, Reason = reason, OccurredAt = now });
        }

        foreach (((Guid coopId, int itemId), int delta) in stock)
        {
            if (delta == 0)
            {
                continue;
            }

            IRepository<CoopStockItem> rows = unit.Repository<CoopStockItem>();
            CoopStockItem? row = await rows.FirstOrDefaultAsync(s => s.CoopId == coopId && s.ItemId == itemId, cancellationToken).ConfigureAwait(false);
            int next = (row?.Quantity ?? 0) + delta;
            if (next < 0)
            {
                return TransferStatus.NotEnoughItems;
            }

            if (row is null)
            {
                rows.Add(new CoopStockItem { CoopId = coopId, ItemId = itemId, Quantity = next });
            }
            else if (next == 0)
            {
                rows.Remove(row);
            }
            else
            {
                row.Quantity = next;
            }
        }

        return TransferStatus.Ok;
    }

    // The player's wallet inside the unit; a new player's gets the starting coins and a log line.
    private async Task<PlayerWallet> WalletAsync(IUnitOfWork unit, Guid playerId, CancellationToken cancellationToken)
    {
        PlayerWallet? wallet = await unit.Repository<PlayerWallet>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
        return wallet ?? this.NewWallet(unit, playerId);
    }

    // Every party's wallet in one query (docs/adr/ADR-035 D1): a two-party sale reads both balances in one round trip. A party without a row gets a new one.
    private async Task<Dictionary<Guid, PlayerWallet>> WalletsAsync(IUnitOfWork unit, IEnumerable<Guid> playerIds, CancellationToken cancellationToken)
    {
        Guid[] ids = [.. playerIds];
        Dictionary<Guid, PlayerWallet> wallets = new(ids.Length);
        if (ids.Length == 0)
        {
            return wallets;
        }

        List<PlayerWallet> found = await unit.Repository<PlayerWallet>().ListAsync(w => ids.Contains(w.PlayerId), cancellationToken).ConfigureAwait(false);
        foreach (PlayerWallet wallet in found)
        {
            wallets[wallet.PlayerId] = wallet;
        }

        foreach (Guid id in ids)
        {
            if (!wallets.ContainsKey(id))
            {
                wallets[id] = this.NewWallet(unit, id);
            }
        }

        return wallets;
    }

    private PlayerWallet NewWallet(IUnitOfWork unit, Guid playerId)
    {
        DateTimeOffset now = _time.GetUtcNow();
        PlayerWallet wallet = new() { PlayerId = playerId, Coins = CoinRules.StartingCoins, CreatedAt = now };
        unit.Repository<PlayerWallet>().Add(wallet);
        unit.Repository<CoinLogEntry>().Add(new CoinLogEntry { PlayerId = playerId, Delta = CoinRules.StartingCoins, Reason = StarterReason, OccurredAt = now });
        return wallet;
    }

    private async Task PushBalanceAsync(Guid playerId, long balance, CancellationToken cancellationToken)
    {
        if (_notifier is null)
        {
            return;
        }

        using PacketScope<WalletUpdate> lease = PacketFactory<WalletUpdate>.Acquire();
        lease.Value.Coins = balance;
        _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
    }
}

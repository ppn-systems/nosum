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
    public async Task<TransferStatus> ExecuteAsync(IReadOnlyList<TransferLeg> legs, string reason, TransferEnlistment? enlist = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(legs);
        ArgumentException.ThrowIfNullOrEmpty(reason);

        // Group the legs: per player the inventory operations and the net coins; per co-op the stock changes.
        Dictionary<Guid, List<InventoryOperation>> adds = [];
        Dictionary<Guid, List<InventoryOperation>> removes = [];
        Dictionary<Guid, long> coins = [];
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
                    removes[leg.Party.Id].Add(InventoryOperation.Remove(leg.ItemId, -leg.Items));
                }

                coins[leg.Party.Id] = coins.GetValueOrDefault(leg.Party.Id) + leg.Coins;
            }
            else if (leg.Items != 0)
            {
                stock[(leg.Party.Id, leg.ItemId)] = stock.GetValueOrDefault((leg.Party.Id, leg.ItemId)) + leg.Items;
            }
        }

        if (players.Count == 0)
        {
            return TransferStatus.Invalid;
        }

        // Removes first, so room freed by giving counts for receiving.
        List<PlayerInventoryChange> changes = [];
        foreach (Guid player in players)
        {
            List<InventoryOperation> operations = [.. removes[player], .. adds[player]];
            changes.Add(new PlayerInventoryChange(player, operations));
        }

        TransferStatus failure = TransferStatus.Ok;
        InventoryStatus status = await _inventory.ApplyManyAsync(
            changes,
            reason,
            async (unit, token) =>
            {
                failure = await this.JoinAsync(unit, coins, stock, reason, token).ConfigureAwait(false);
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
            InventoryStatus.NotEnough => TransferStatus.NotEnoughItems,
            InventoryStatus.NoSpace => TransferStatus.NoSpace,
            InventoryStatus.Conflict when failure != TransferStatus.Ok => failure,
            InventoryStatus.Conflict => TransferStatus.Conflict,
            _ => TransferStatus.Invalid,
        };

        if (result == TransferStatus.Ok)
        {
            foreach ((Guid player, long delta) in coins)
            {
                if (delta != 0)
                {
                    await this.PushBalanceAsync(player, cancellationToken).ConfigureAwait(false);
                }
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

    private async Task<TransferStatus> JoinAsync(IUnitOfWork unit, Dictionary<Guid, long> coins, Dictionary<(Guid Coop, int Item), int> stock, string reason, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();

        foreach ((Guid player, long delta) in coins)
        {
            // The wallet is created (and the starting coins logged) even by a transfer that moves no coins for this player:
            // the balance row must exist before anything reads it.
            PlayerWallet wallet = await this.WalletAsync(unit, player, cancellationToken).ConfigureAwait(false);
            if (delta == 0)
            {
                continue;
            }

            long next = wallet.Coins + delta;
            if (next < 0)
            {
                return TransferStatus.NotEnoughCoins;
            }

            if (next > CoinRules.MaxBalance)
            {
                return TransferStatus.TooBig;
            }

            wallet.Coins = next;
            unit.Repository<CoinLogEntry>().Add(new CoinLogEntry { PlayerId = player, Delta = delta, Reason = reason, OccurredAt = now });
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
        IRepository<PlayerWallet> wallets = unit.Repository<PlayerWallet>();
        PlayerWallet? wallet = await wallets.FindAsync(playerId, cancellationToken).ConfigureAwait(false);
        if (wallet is not null)
        {
            return wallet;
        }

        DateTimeOffset now = _time.GetUtcNow();
        wallet = new PlayerWallet { PlayerId = playerId, Coins = CoinRules.StartingCoins, CreatedAt = now };
        wallets.Add(wallet);
        unit.Repository<CoinLogEntry>().Add(new CoinLogEntry { PlayerId = playerId, Delta = CoinRules.StartingCoins, Reason = StarterReason, OccurredAt = now });
        return wallet;
    }

    private async Task PushBalanceAsync(Guid playerId, CancellationToken cancellationToken)
    {
        if (_notifier is null)
        {
            return;
        }

        long balance = await this.GetBalanceAsync(playerId, cancellationToken).ConfigureAwait(false);
        using PacketScope<WalletUpdate> lease = PacketFactory<WalletUpdate>.Acquire();
        lease.Value.Coins = balance;
        _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
    }
}

// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Inventory;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Shops;
using Nosum.Infrastructure.Shops;

namespace Nosum.Infrastructure.Inventory;

/// <summary>
/// <see cref="IBackpackService"/> (docs/adr/ADR-081). A purchase is one transfer: the coins leave the player and the bag grows in the same transaction
/// (<see cref="IInventoryService"/> refuses a growth that is not larger than the bag it finds, so two purchases of one size cannot both succeed). The
/// price and the sizes come from <see cref="BackpackRules"/>; the player must stand at the tailor's counter.
/// </summary>
public sealed class BackpackService : IBackpackService
{
    private const string UpgradeReason = "backpack.upgrade";

    private readonly ITransferService _transfers;
    private readonly IInventoryService _inventory;
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;

    /// <summary>Initializes the service.</summary>
    /// <param name="transfers">Takes the coins and grows the bag in one transaction.</param>
    /// <param name="inventory">Reads the bag's size.</param>
    /// <param name="presence">Where players stand.</param>
    /// <param name="maps">The hosted maps (tile size).</param>
    public BackpackService(ITransferService transfers, IInventoryService inventory, IMapPresence presence, IMapCatalog maps)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);

        _transfers = transfers;
        _inventory = inventory;
        _presence = presence;
        _maps = maps;
    }

    /// <inheritdoc/>
    public async Task<BackpackView> GetAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!this.TryPlayerAtCounter(connection, out Guid playerId))
        {
            return new BackpackView(BackpackStatus.NotAtCounter, 0, 0, 0);
        }

        return await this.ViewAsync(playerId, BackpackStatus.Ok, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BackpackView> UpgradeAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!this.TryPlayerAtCounter(connection, out Guid playerId))
        {
            return new BackpackView(BackpackStatus.NotAtCounter, 0, 0, 0);
        }

        InventoryView bag = await _inventory.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        if (!BackpackRules.TryNext(bag.Capacity, out BackpackQuote quote))
        {
            return await this.ViewAsync(playerId, BackpackStatus.MaxSize, cancellationToken).ConfigureAwait(false);
        }

        // The bag may have grown since the read above; the growth is refused then, so the price is never paid twice for one size.
        TransferStatus status = await _transfers.ExecuteWithOperationsAsync(
            playerId,
            [InventoryOperation.Grow(quote.Capacity)],
            [new TransferLeg(TransferParty.Player(playerId), 0, 0, -quote.Coins)],
            UpgradeReason,
            null,
            cancellationToken).ConfigureAwait(false);

        return await this.ViewAsync(playerId, status switch
        {
            TransferStatus.Ok => BackpackStatus.Ok,
            TransferStatus.NotEnoughCoins => BackpackStatus.NotEnoughCoins,
            _ => BackpackStatus.Conflict,
        }, cancellationToken).ConfigureAwait(false);
    }

    // The player whose record places them at the tailor's counter; the answer comes from the server's own presence, never from the packet.
    private bool TryPlayerAtCounter(IConnection connection, out Guid playerId)
    {
        playerId = Guid.Empty;
        if (!_presence.TryGet(connection, out PlayerPresence? player) || !CounterReach.IsAtCounter(_maps, player, ShopCatalog.ClothingShop))
        {
            return false;
        }

        playerId = player.AccountId;
        return true;
    }

    // The state after an attempt: the size the bag has now and the next size with its price.
    private async Task<BackpackView> ViewAsync(Guid playerId, BackpackStatus status, CancellationToken cancellationToken)
    {
        InventoryView bag = await _inventory.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        return BackpackRules.TryNext(bag.Capacity, out BackpackQuote next)
            ? new BackpackView(status, bag.Capacity, next.Capacity, next.Coins)
            : new BackpackView(status, bag.Capacity, 0, 0);
    }
}

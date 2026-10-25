// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Shops;
using Nosum.Gameplay.Items;

namespace Nosum.Infrastructure.Dev;

/// <summary><see cref="IDevGrantService"/> on top of <see cref="ITransferService"/>. Registered by the host only in Development.</summary>
public sealed class DevGrantService : IDevGrantService
{
    private const long MaxCoins = 1_000_000_000;

    private readonly ITransferService _transfers;

    /// <summary>Initializes the service.</summary>
    /// <param name="transfers">Moves coins and items atomically.</param>
    public DevGrantService(ITransferService transfers)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        _transfers = transfers;
    }

    /// <inheritdoc/>
    public async Task<bool> GrantAsync(Guid playerId, int itemId, int quantity, long coins, CancellationToken cancellationToken = default)
    {
        if (coins < 0 || coins > MaxCoins)
        {
            return false;
        }

        int items = 0;
        if (itemId != 0)
        {
            if (!ItemCatalog.TryGet(itemId, out ItemDefinition? item) || quantity < 1 || quantity > item.MaxStack)
            {
                return false;
            }

            items = quantity;
        }
        else if (coins == 0)
        {
            return false;
        }

        TransferStatus status = await _transfers.ExecuteAsync(
            [new TransferLeg(TransferParty.Player(playerId), itemId, items, coins)],
            "dev.grant",
            null,
            cancellationToken).ConfigureAwait(false);
        return status == TransferStatus.Ok;
    }
}
